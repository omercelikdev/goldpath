using CsCheck;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Goldpath.Archival.Tests;

public class RetentionAndJobsTests
{
    [Fact]
    public async Task Row_retention_purges_only_guarded_aged_rows_in_bounded_batches()
    {
        using var fixture = new ArchiveFixture();
        var old = DateTimeOffset.UtcNow.AddDays(-200);
        fixture.Mutate(db =>
        {
            for (var i = 0; i < 10; i++)
            {
                db.Usage.Add(new UsageDetail { RecordedAt = old, RolledUp = true });      // purgeable
            }

            db.Usage.Add(new UsageDetail { RecordedAt = old, RolledUp = false });         // guarded
            db.Usage.Add(new UsageDetail { RecordedAt = DateTimeOffset.UtcNow, RolledUp = true });   // young
        });

        var options = new GoldpathArchivalOptions();
        options.AddRowRetention<UsageDetail>(r => r
            .After(TimeSpan.FromDays(90), u => u.RecordedAt)
            .Where(u => u.RolledUp));
        var retention = options.RowRetentions[0];
        Assert.True(retention.HasGuard);

        using var scope = fixture.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ArchiveTestContext>();
        Assert.Equal(10, await retention.CountDueAsync(db, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(4, await retention.PurgeBatchAsync(db, DateTimeOffset.UtcNow, 4, CancellationToken.None));
        Assert.Equal(6, await retention.PurgeBatchAsync(db, DateTimeOffset.UtcNow, 100, CancellationToken.None));
        Assert.Equal(0, await retention.PurgeBatchAsync(db, DateTimeOffset.UtcNow, 100, CancellationToken.None));

        var survivors = fixture.Query(q => q.Usage.ToList());
        Assert.Equal(2, survivors.Count);
        Assert.Contains(survivors, u => !u.RolledUp);                       // the guard held
        Assert.Contains(survivors, u => u.RecordedAt > old.AddDays(1));     // the young row held
    }

    [Fact]
    public async Task The_three_runs_execute_end_to_end_on_the_jobs_runner()
    {
        using var fixture = new ArchiveFixture();
        for (var i = 0; i < 5; i++)
        {
            fixture.SeedClaim();
        }

        var archival = new GoldpathArchivalOptions { BatchSize = 2 };
        archival.AddArchive<Claim>(a => a
            .Graph(c => c.Decisions, c => c.Documents)
            .Key(c => c.Id)
            .DueWhen(c => c.ClosedAt != null, c => c.ClosedAt!.Value)
            .ArchiveAfter(TimeSpan.FromDays(30))
            .RetainFor(years: 0)                     // expire instantly so the purge has work
            .DeleteHotRowsAfterArchive());

        var jobs = new GoldpathJobsOptions();
        jobs.AddGoldpathArchivalJobs<ArchiveTestContext>();
        Assert.Equal(3, jobs.Jobs.Count);
        var archiveDef = jobs.Jobs.Single(j => j.JobType == typeof(GoldpathArchiveJob<ArchiveTestContext>));
        var purgeDef = jobs.Jobs.Single(j => j.JobType == typeof(GoldpathRetentionPurgeJob<ArchiveTestContext>));
        var verifyDef = jobs.Jobs.Single(j => j.JobType == typeof(GoldpathArchiveVerifyJob<ArchiveTestContext>));
        Assert.Equal(1, archiveDef.MaxParallelChunks);                       // single-writer chain
        Assert.Contains(archiveDef.JobType.Name, purgeDef.StartAfterJobs[0], StringComparison.Ordinal);
        Assert.NotNull(archiveDef.Deadline);                                 // GP1302 satisfied by default

        var runner = new GoldpathJobRunner<ArchiveTestContext>(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<GoldpathJobRunner<ArchiveTestContext>>.Instance);
        var engine = fixture.Engine;

        // ARCHIVE: 5 claims / batch 2 → 3 chunks, all moved.
        var archiveJob = new GoldpathArchiveJob<ArchiveTestContext>(engine, archival);
        var status = await runner.RunAsync(archiveJob, archiveDef, new GoldpathFireFacts("arch", "n1", "f1", false), CancellationToken.None);
        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        Assert.Equal(5, fixture.Query(db => db.Set<GoldpathArchiveEntry>().Count()));
        Assert.Equal(0, fixture.Query(db => db.Claims.Count()));

        // VERIFY: clean chain → no repair items.
        var verifyJob = new GoldpathArchiveVerifyJob<ArchiveTestContext>(engine, archival);
        status = await runner.RunAsync(verifyJob, verifyDef, new GoldpathFireFacts("arch", "n1", "f2", false), CancellationToken.None);
        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        Assert.Equal(0, fixture.Query(db => db.Set<GoldpathJobItemFailure>().Count()));

        // Tamper, then VERIFY again: the finding lands in the repair queue.
        fixture.Mutate(db =>
        {
            var entry = db.Set<GoldpathArchiveEntry>().First(e => e.ChainIndex == 3);
            entry.Document += " ";
        });
        status = await runner.RunAsync(verifyJob, verifyDef, new GoldpathFireFacts("arch", "n1", "f3", false), CancellationToken.None);
        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        var tamper = fixture.Query(db => db.Set<GoldpathJobItemFailure>().Single());
        Assert.Equal("Claim#3", tamper.ItemKey);

        // PURGE: everything expired (retain 0) → all entries leave, chain state remembers.
        var purgeJob = new GoldpathRetentionPurgeJob<ArchiveTestContext>(engine, archival, TimeProvider.System);
        status = await runner.RunAsync(purgeJob, purgeDef, new GoldpathFireFacts("arch", "n1", "f4", false), CancellationToken.None);
        Assert.Equal(GoldpathJobRunStatus.Completed, status);
        Assert.Equal(0, fixture.Query(db => db.Set<GoldpathArchiveEntry>().Count()));
        Assert.Equal(5, fixture.Query(db => db.Set<GoldpathArchiveChainState>().Single().PurgedThroughIndex));
    }

    [Fact]
    public void Builders_refuse_unmodeled_lifecycles()
    {
        var options = new GoldpathArchivalOptions();
        Assert.Throws<InvalidOperationException>(() =>
            options.AddArchive<Claim>(a => a.Key(c => c.Id)));               // no DueWhen
        Assert.Throws<InvalidOperationException>(() =>
            options.AddRowRetention<UsageDetail>(r => r.Where(u => u.RolledUp)));   // no After
    }

    [Fact]
    public void Envelope_hashes_are_canonical_and_deterministic()
    {
        var content = GoldpathArchiveEnvelope.ContentHash(1, "Claim", "k1", "t1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "{\"a\":1}");
        var again = GoldpathArchiveEnvelope.ContentHash(1, "Claim", "k1", "t1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "{\"a\":1}");
        Assert.Equal(content, again);
        Assert.NotEqual(content, GoldpathArchiveEnvelope.ContentHash(1, "Claim", "k1", "t1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "{\"a\":2}"));
        Assert.NotEqual(content, GoldpathArchiveEnvelope.ContentHash(1, "Claim", "k1", null, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "{\"a\":1}"));
        var chain = GoldpathArchiveEnvelope.ChainHash(content, "", 1);
        Assert.NotEqual(chain, GoldpathArchiveEnvelope.ChainHash(content, "", 2));
        Assert.NotEqual(chain, GoldpathArchiveEnvelope.ChainHash(content, chain, 1));
    }

    [Fact]
    public void Any_single_character_document_mutation_changes_the_content_hash()
    {
        // Property: the canonical hash detects arbitrary single-character corruption —
        // the verify job's whole value rests on this.
        var gen = Gen.Select(Gen.String[Gen.Char.AlphaNumeric, 1, 200], Gen.Int[0, 199], Gen.Char.AlphaNumeric);
        gen.Sample(tuple =>
        {
            var (document, position, replacement) = tuple;
            if (position >= document.Length || document[position] == replacement)
            {
                return true;   // no-op mutation — nothing to detect
            }

            var mutated = document[..position] + replacement + document[(position + 1)..];
            return GoldpathArchiveEnvelope.ContentHash(1, "d", "k", null, default, document)
                != GoldpathArchiveEnvelope.ContentHash(1, "d", "k", null, default, mutated);
        });
    }

    [Fact]
    public async Task Round_trip_preserves_the_graph_for_arbitrary_contents()
    {
        // Property: serialize → archive → retrieve → deserialize is identity on the fields
        // that matter (reference, tenant, child counts and payloads).
        var gen = Gen.Select(
            Gen.String[Gen.Char.AlphaNumeric, 0, 40],
            Gen.String[Gen.Char.AlphaNumeric, 0, 20].List[0, 5],
            Gen.String[Gen.Char.AlphaNumeric, 0, 20].List[0, 3]);
        await gen.SampleAsync(async tuple =>
        {
            var (reference, verdicts, docs) = tuple;
            using var fixture = new ArchiveFixture();
            var claim = new Claim
            {
                Id = Guid.NewGuid(),
                Reference = reference,
                TenantId = "t",
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-90),
                Decisions = verdicts.Select(v => new Decision { Id = Guid.NewGuid(), Verdict = v }).ToList(),
                Documents = docs.Select(d => new ClaimDocument { Id = Guid.NewGuid(), Name = d }).ToList(),
            };
            fixture.Mutate(db => db.Claims.Add(claim));

            var definition = ArchiveFixture.ClaimArchive();
            using var scope = fixture.Scope();
            await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 10, CancellationToken.None);
            var entry = await fixture.Engine.RetrieveAsync(scope.ServiceProvider, "Claim", claim.Id.ToString(), CancellationToken.None);
            if (entry is null)
            {
                return false;
            }

            var restored = System.Text.Json.JsonSerializer.Deserialize<Claim>(entry.Document)!;
            return restored.Reference == reference
                && restored.Decisions.Select(d => d.Verdict).OrderBy(v => v, StringComparer.Ordinal)
                    .SequenceEqual(verdicts.OrderBy(v => v, StringComparer.Ordinal))
                && restored.Documents.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal)
                    .SequenceEqual(docs.OrderBy(n => n, StringComparer.Ordinal));
        }, iter: 20);   // each sample builds a database — keep the count honest but bounded
    }
}
