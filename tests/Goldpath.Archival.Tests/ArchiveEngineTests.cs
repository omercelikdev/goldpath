using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Goldpath.Archival.Tests;

public class ArchiveEngineTests
{
    [Fact]
    public async Task Archive_moves_the_whole_graph_and_seals_the_chain()
    {
        using var fixture = new ArchiveFixture();
        var claim = fixture.SeedClaim(decisions: 2, documents: 1, tenant: "agency-7");
        var definition = ArchiveFixture.ClaimArchive();

        using var scope = fixture.Scope();
        var moved = await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 10, CancellationToken.None);

        Assert.Equal(1, moved);
        var entry = fixture.Query(db => db.Set<GoldpathArchiveEntry>().Single());
        Assert.Equal("Claim", entry.Definition);
        Assert.Equal(claim.Id.ToString(), entry.AggregateKey);
        Assert.Equal("agency-7", entry.Tenant);
        Assert.Equal(1, entry.ChainIndex);
        Assert.Equal("", entry.PreviousHash);
        Assert.Equal(64, entry.ContentHash.Length);
        Assert.Equal(64, entry.ChainHash.Length);

        // The DOCUMENT carries the whole file: root + decisions + documents (graph-scoped).
        var document = JsonSerializer.Deserialize<Claim>(entry.Document)!;
        Assert.Equal(claim.Reference, document.Reference);
        Assert.Equal(2, document.Decisions.Count);
        Assert.Single(document.Documents);

        // Move, not copy: the hot graph is gone (cascade took the children).
        Assert.Equal(0, fixture.Query(db => db.Claims.Count()));
        Assert.Equal(0, fixture.Query(db => db.Set<Decision>().Count()));

        var state = fixture.Query(db => db.Set<GoldpathArchiveChainState>().Single());
        Assert.Equal(1, state.LastIndex);
        Assert.Equal(entry.ChainHash, state.LastHash);
    }

    [Fact]
    public async Task The_chain_links_across_batches_and_verifies_clean()
    {
        using var fixture = new ArchiveFixture();
        for (var i = 0; i < 3; i++)
        {
            fixture.SeedClaim();
        }

        var definition = ArchiveFixture.ClaimArchive();
        using (var scope = fixture.Scope())
        {
            await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 2, CancellationToken.None);
        }

        using (var scope = fixture.Scope())
        {
            await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 2, CancellationToken.None);
        }

        var entries = fixture.Query(db => db.Set<GoldpathArchiveEntry>().OrderBy(e => e.ChainIndex).ToList());
        Assert.Equal(3, entries.Count);
        Assert.Equal("", entries[0].PreviousHash);
        Assert.Equal(entries[0].ChainHash, entries[1].PreviousHash);
        Assert.Equal(entries[1].ChainHash, entries[2].PreviousHash);

        using var verifyScope = fixture.Scope();
        var findings = await fixture.Engine.VerifySliceAsync(verifyScope.ServiceProvider, definition, 1, 3, CancellationToken.None);
        Assert.Empty(findings);
    }

    [Fact]
    public async Task Verify_detects_document_tamper_and_silent_restamps()
    {
        using var fixture = new ArchiveFixture();
        fixture.SeedClaim();
        fixture.SeedClaim();
        var definition = ArchiveFixture.ClaimArchive();
        using (var scope = fixture.Scope())
        {
            await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 10, CancellationToken.None);
        }

        // Tamper 1: mutate the document (the classic UPDATE from a hostile DBA).
        fixture.Mutate(db =>
        {
            var entry = db.Set<GoldpathArchiveEntry>().First(e => e.ChainIndex == 1);
            entry.Document = entry.Document.Replace("verdict-0", "verdict-FORGED", StringComparison.Ordinal);
        });

        // Tamper 2: mutate AND re-stamp the content hash — but with no erasure record.
        fixture.Mutate(db =>
        {
            var entry = db.Set<GoldpathArchiveEntry>().First(e => e.ChainIndex == 2);
            entry.Document = entry.Document.Replace("doc-0", "doc-REPLACED", StringComparison.Ordinal);
            entry.ContentHash = GoldpathArchiveEnvelope.ContentHash(
                entry.SchemaVersion, entry.Definition, entry.AggregateKey, entry.Tenant, entry.DueAt, entry.Document);
        });

        using var verifyScope = fixture.Scope();
        var findings = await fixture.Engine.VerifySliceAsync(verifyScope.ServiceProvider, definition, 1, 2, CancellationToken.None);

        Assert.Contains(findings, f => f.ChainIndex == 1 && f.Problem.Contains("content hash", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.ChainIndex == 2 && f.Problem.Contains("without an erasure record", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_reports_a_missing_middle_entry()
    {
        using var fixture = new ArchiveFixture();
        for (var i = 0; i < 3; i++)
        {
            fixture.SeedClaim();
        }

        var definition = ArchiveFixture.ClaimArchive();
        using (var scope = fixture.Scope())
        {
            await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 10, CancellationToken.None);
        }

        fixture.Mutate(db => db.RemoveRange(db.Set<GoldpathArchiveEntry>().Where(e => e.ChainIndex == 2)));

        using var verifyScope = fixture.Scope();
        var findings = await fixture.Engine.VerifySliceAsync(verifyScope.ServiceProvider, definition, 1, 3, CancellationToken.None);
        Assert.Contains(findings, f => f.Problem.Contains("missing entry", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Purge_removes_only_a_contiguous_prefix_and_a_hold_stops_it()
    {
        using var fixture = new ArchiveFixture();
        for (var i = 0; i < 3; i++)
        {
            fixture.SeedClaim();
        }

        var definition = ArchiveFixture.ClaimArchive(retainYears: 0);   // everything expires instantly
        using (var scope = fixture.Scope())
        {
            await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 10, CancellationToken.None);
        }

        var heldKey = fixture.Query(db => db.Set<GoldpathArchiveEntry>().Single(e => e.ChainIndex == 2).AggregateKey);
        fixture.Mutate(db => db.Add(new GoldpathLegalHold
        {
            Definition = "Claim",
            AggregateKey = heldKey,
            CaseReference = "CASE-42",
            PlacedBy = "counsel",
            PlacedAt = DateTimeOffset.UtcNow,
        }));

        using (var scope = fixture.Scope())
        {
            var purged = await fixture.Engine.PurgeExpiredEntriesAsync(scope.ServiceProvider, definition, 10, CancellationToken.None);
            Assert.Equal(1, purged);   // only the prefix BEFORE the held entry leaves
        }

        var remaining = fixture.Query(db => db.Set<GoldpathArchiveEntry>().Select(e => e.ChainIndex).OrderBy(i => i).ToList());
        Assert.Equal([2L, 3L], remaining);
        var state = fixture.Query(db => db.Set<GoldpathArchiveChainState>().Single());
        Assert.Equal(1, state.PurgedThroughIndex);

        // The chain STILL verifies — the purged head anchors the first kept entry.
        using var verifyScope = fixture.Scope();
        var findings = await fixture.Engine.VerifySliceAsync(verifyScope.ServiceProvider, definition, 2, 3, CancellationToken.None);
        Assert.Empty(findings);

        // Lift the hold → the rest purges.
        fixture.Mutate(db =>
        {
            var hold = db.Set<GoldpathLegalHold>().Single();
            hold.LiftedAt = DateTimeOffset.UtcNow;
            hold.LiftedBy = "counsel";
        });
        using (var scope = fixture.Scope())
        {
            Assert.Equal(2, await fixture.Engine.PurgeExpiredEntriesAsync(scope.ServiceProvider, definition, 10, CancellationToken.None));
        }

        Assert.Equal(0, fixture.Query(db => db.Set<GoldpathArchiveEntry>().Count()));
    }

    [Fact]
    public async Task Copy_mode_keeps_hot_rows_and_never_rearchives()
    {
        using var fixture = new ArchiveFixture();
        fixture.SeedClaim();
        var definition = ArchiveFixture.ClaimArchive(deleteHot: false);

        using (var scope = fixture.Scope())
        {
            Assert.Equal(1, await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 10, CancellationToken.None));
        }

        Assert.Equal(1, fixture.Query(db => db.Claims.Count()));   // copy, not move

        using (var scope = fixture.Scope())
        {
            Assert.Equal(0, await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 10, CancellationToken.None));
            var db = scope.ServiceProvider.GetRequiredService<ArchiveTestContext>();
            Assert.Equal(0, await fixture.Engine.CountDueAsync(db, definition, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Open_claims_are_never_due()
    {
        using var fixture = new ArchiveFixture();
        fixture.Mutate(db => db.Claims.Add(new Claim { Id = Guid.NewGuid(), Reference = "OPEN", ClosedAt = null }));
        fixture.SeedClaim(closedAt: DateTimeOffset.UtcNow.AddDays(-5));   // closed but inside the hot period

        var definition = ArchiveFixture.ClaimArchive();
        using var scope = fixture.Scope();
        Assert.Equal(0, await fixture.Engine.ArchiveNextBatchAsync(scope.ServiceProvider, definition, 10, CancellationToken.None));
    }
}
