using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Goldpath.Jobs.Tests;

/// <summary>
/// The run-model MAPPING is a contract: the claim protocol rides the Status concurrency
/// token, resume rides the (RunId, Status) index, admin queries ride the rest. Locked via
/// EF metadata so a silently-dropped token or index dies here, not in production.
/// </summary>
public class ModelContractTests
{
    private static readonly Microsoft.EntityFrameworkCore.Metadata.IModel Model = BuildModel();

    private static Microsoft.EntityFrameworkCore.Metadata.IModel BuildModel()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        using var context = new JobsTestContext(
            new DbContextOptionsBuilder<JobsTestContext>().UseSqlite(connection).Options);
        return context.Model;
    }

    [Fact]
    public void Every_table_name_and_max_length_is_a_contract()
    {
        // The mapping IS the schema (migrations RFC D1): a silently-changed name or
        // length is a production ALTER nobody reviewed.
        var run = Model.FindEntityType(typeof(GoldpathJobRun))!;
        Assert.Equal("GoldpathJobRuns", run.GetTableName());
        Assert.Equal(120, run.FindProperty("SchedulerName")!.GetMaxLength());
        Assert.Equal(150, run.FindProperty("JobName")!.GetMaxLength());
        Assert.Equal(16, run.FindProperty("Status")!.GetMaxLength());

        var chunk = Model.FindEntityType(typeof(GoldpathJobRunChunk))!;
        Assert.Equal("GoldpathJobRunChunks", chunk.GetTableName());
        Assert.Equal(16, chunk.FindProperty("Status")!.GetMaxLength());

        var failure = Model.FindEntityType(typeof(GoldpathJobItemFailure))!;
        Assert.Equal("GoldpathJobItemFailures", failure.GetTableName());
        Assert.Equal(256, failure.FindProperty("ItemKey")!.GetMaxLength());

        var audit = Model.FindEntityType(typeof(GoldpathJobAdminAudit))!;
        Assert.Equal("GoldpathJobAdminAudit", audit.GetTableName());
        Assert.Equal(256, audit.FindProperty("Actor")!.GetMaxLength());
        Assert.Equal(32, audit.FindProperty("Action")!.GetMaxLength());
        Assert.Equal(120, audit.FindProperty("Fleet")!.GetMaxLength());
        Assert.Equal(256, audit.FindProperty("Target")!.GetMaxLength());

        var execution = Model.FindEntityType(typeof(GoldpathJobExecution))!;
        Assert.Equal("GoldpathJobExecutions", execution.GetTableName());
        Assert.Equal(120, execution.FindProperty("SchedulerName")!.GetMaxLength());
        Assert.Equal(150, execution.FindProperty("JobName")!.GetMaxLength());
        Assert.Equal(16, execution.FindProperty("Outcome")!.GetMaxLength());
    }

    [Fact]
    public void Every_index_is_a_contract()
    {
        static List<List<string>> Indexes(Microsoft.EntityFrameworkCore.Metadata.IEntityType e)
            => [.. e.GetIndexes().Select(i => i.Properties.Select(p => p.Name).ToList())];

        Assert.Equal([["SchedulerName", "JobName", "Status"], ["StartedAt"]],
            Indexes(Model.FindEntityType(typeof(GoldpathJobRun))!)
                .OrderBy(i => i.Count == 1).ToList());

        var chunkIndexes = Model.FindEntityType(typeof(GoldpathJobRunChunk))!.GetIndexes().ToList();
        Assert.Equal(2, chunkIndexes.Count);
        var resume = Assert.Single(chunkIndexes, i => i.Properties.Select(p => p.Name).SequenceEqual(["RunId", "Status"]));
        Assert.False(resume.IsUnique);
        var plan = Assert.Single(chunkIndexes, i => i.Properties.Select(p => p.Name).SequenceEqual(["RunId", "Index"]));
        Assert.True(plan.IsUnique, "one chunk per plan position — the claim protocol depends on it");

        Assert.Equal([["RunId"]], Indexes(Model.FindEntityType(typeof(GoldpathJobItemFailure))!));
        Assert.Equal([["At"]], Indexes(Model.FindEntityType(typeof(GoldpathJobAdminAudit))!));
        Assert.Equal([["SchedulerName", "JobName"], ["FiredAt"]],
            Indexes(Model.FindEntityType(typeof(GoldpathJobExecution))!)
                .OrderBy(i => i.Count == 1).ToList());
    }

    [Fact]
    public void Status_constants_are_persistence_contracts()
    {
        // These strings live in ROWS: changing one silently orphans every existing run.
        Assert.Equal("Running", GoldpathJobRunStatus.Running);
        Assert.Equal("Completed", GoldpathJobRunStatus.Completed);
        Assert.Equal("Failed", GoldpathJobRunStatus.Failed);
        Assert.Equal("Pending", GoldpathJobChunkStatus.Pending);
        Assert.Equal("Claimed", GoldpathJobChunkStatus.Claimed);
        Assert.Equal("Completed", GoldpathJobChunkStatus.Completed);
        Assert.Equal("Failed", GoldpathJobChunkStatus.Failed);
    }

    [Fact]
    public void Entity_defaults_are_contracts()
    {
        var run = new GoldpathJobRun();
        Assert.Equal("", run.SchedulerName);
        Assert.Equal("", run.JobName);
        Assert.Equal(GoldpathJobRunStatus.Running, run.Status);   // a NEW run row IS a running run

        var chunk = new GoldpathJobRunChunk();
        Assert.Equal("", chunk.Payload);
        Assert.Equal(GoldpathJobChunkStatus.Pending, chunk.Status);   // claimable from birth

        var failure = new GoldpathJobItemFailure();
        Assert.Equal("", failure.ItemKey);
        Assert.Equal("", failure.Reason);

        var audit = new GoldpathJobAdminAudit();
        Assert.Equal("", audit.Actor);
        Assert.Equal("", audit.Action);
        Assert.Equal("", audit.Fleet);
        Assert.Equal("", audit.Target);

        var execution = new GoldpathJobExecution();
        Assert.Equal("", execution.SchedulerName);
        Assert.Equal("", execution.JobName);
        Assert.Equal("", execution.InstanceName);
        Assert.Equal("", execution.Outcome);
    }

    private sealed class ExcludingContext(DbContextOptions<ExcludingContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddGoldpathJobs(excludeFromMigrations: true);
    }

    [Fact]
    public void A_secondary_head_maps_the_tables_but_never_owns_their_migrations()
    {
        // Migrations RFC D3: one table set, ONE owner — a jobs worker running its own
        // fleet sees the shared tables without generating DDL for them.
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        using var context = new ExcludingContext(
            new DbContextOptionsBuilder<ExcludingContext>().UseSqlite(connection).Options);

        // Migration facts live on the DESIGN-TIME model (the one `dotnet ef` reads).
        var designModel = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(context).Model;
        var entityTypes = designModel.GetEntityTypes().ToList();
        Assert.NotEmpty(entityTypes);                       // the model is COMPLETE (queries work)
        Assert.All(entityTypes, e => Assert.True(e.IsTableExcludedFromMigrations(),
            $"{e.ClrType.Name} would generate DDL from a non-owner context"));

        // The OWNER context keeps full ownership — the default changes nothing.
        using var owner = new JobsTestContext(
            new DbContextOptionsBuilder<JobsTestContext>().UseSqlite(connection).Options);
        var ownerModel = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(owner).Model;
        Assert.All(ownerModel.GetEntityTypes(), e => Assert.False(e.IsTableExcludedFromMigrations()));
    }

    [Fact]
    public void Chunk_status_is_the_claim_concurrency_token()
    {
        var status = Model.FindEntityType(typeof(GoldpathJobRunChunk))!.FindProperty(nameof(GoldpathJobRunChunk.Status))!;
        Assert.True(status.IsConcurrencyToken, "claims race safely ONLY through this token");
        Assert.Equal(16, status.GetMaxLength());
    }

    [Fact]
    public void Chunks_are_unique_per_run_position_and_indexed_for_claims()
    {
        var chunk = Model.FindEntityType(typeof(GoldpathJobRunChunk))!;
        Assert.Equal("GoldpathJobRunChunks", chunk.GetTableName());
        Assert.Contains(chunk.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual([nameof(GoldpathJobRunChunk.RunId), nameof(GoldpathJobRunChunk.Index)]));
        Assert.Contains(chunk.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual([nameof(GoldpathJobRunChunk.RunId), nameof(GoldpathJobRunChunk.Status)]));
    }

    [Fact]
    public void Runs_carry_the_open_run_lookup_index_and_bounded_names()
    {
        var run = Model.FindEntityType(typeof(GoldpathJobRun))!;
        Assert.Equal("GoldpathJobRuns", run.GetTableName());
        Assert.Contains(run.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(GoldpathJobRun.SchedulerName), nameof(GoldpathJobRun.JobName), nameof(GoldpathJobRun.Status)]));
        Assert.Contains(run.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(GoldpathJobRun.StartedAt)]));
        Assert.Equal(120, run.FindProperty(nameof(GoldpathJobRun.SchedulerName))!.GetMaxLength());
        Assert.Equal(150, run.FindProperty(nameof(GoldpathJobRun.JobName))!.GetMaxLength());
        Assert.Equal(16, run.FindProperty(nameof(GoldpathJobRun.Status))!.GetMaxLength());
    }

    [Fact]
    public void Repair_queue_and_history_are_mapped_and_queryable_by_their_hot_paths()
    {
        var failure = Model.FindEntityType(typeof(GoldpathJobItemFailure))!;
        Assert.Equal("GoldpathJobItemFailures", failure.GetTableName());
        Assert.Equal(256, failure.FindProperty(nameof(GoldpathJobItemFailure.ItemKey))!.GetMaxLength());
        Assert.Contains(failure.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(GoldpathJobItemFailure.RunId)]));

        var execution = Model.FindEntityType(typeof(GoldpathJobExecution))!;
        Assert.Equal("GoldpathJobExecutions", execution.GetTableName());
        Assert.Equal(120, execution.FindProperty(nameof(GoldpathJobExecution.SchedulerName))!.GetMaxLength());
        Assert.Equal(150, execution.FindProperty(nameof(GoldpathJobExecution.JobName))!.GetMaxLength());
        Assert.Equal(16, execution.FindProperty(nameof(GoldpathJobExecution.Outcome))!.GetMaxLength());
        Assert.Contains(execution.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(GoldpathJobExecution.SchedulerName), nameof(GoldpathJobExecution.JobName)]));
        Assert.Contains(execution.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(GoldpathJobExecution.FiredAt)]));
    }

    [Fact]
    public void Admin_audit_is_mapped_with_bounded_columns_and_a_time_index()
    {
        var audit = Model.FindEntityType(typeof(GoldpathJobAdminAudit))!;
        Assert.Equal("GoldpathJobAdminAudit", audit.GetTableName());
        Assert.Equal(256, audit.FindProperty(nameof(GoldpathJobAdminAudit.Actor))!.GetMaxLength());
        Assert.Equal(32, audit.FindProperty(nameof(GoldpathJobAdminAudit.Action))!.GetMaxLength());
        Assert.Equal(120, audit.FindProperty(nameof(GoldpathJobAdminAudit.Fleet))!.GetMaxLength());
        Assert.Equal(256, audit.FindProperty(nameof(GoldpathJobAdminAudit.Target))!.GetMaxLength());
        Assert.Contains(audit.GetIndexes(), i => i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(GoldpathJobAdminAudit.At)]));

        // Replay bookkeeping rides the repair queue.
        var failure = Model.FindEntityType(typeof(GoldpathJobItemFailure))!;
        Assert.NotNull(failure.FindProperty(nameof(GoldpathJobItemFailure.RedrivenAt)));
    }

    [Fact]
    public void Entity_defaults_are_the_states_the_runner_relies_on()
    {
        // A freshly-added run IS Running and a freshly-planned chunk IS Pending — the
        // open-run lookup and the claim loop both key on these defaults.
        var run = new GoldpathJobRun();
        Assert.Equal(GoldpathJobRunStatus.Running, run.Status);
        Assert.Equal("", run.SchedulerName);
        Assert.Equal("", run.JobName);
        Assert.Null(run.FinishedAt);
        Assert.Equal(0, run.CompletedChunks);
        Assert.Equal(0, run.FailedChunks);
        Assert.Equal(0, run.ItemFailures);
        Assert.Equal(0, run.Executions);

        var chunk = new GoldpathJobRunChunk();
        Assert.Equal(GoldpathJobChunkStatus.Pending, chunk.Status);
        Assert.Equal("", chunk.Payload);
        Assert.Equal(0, chunk.Attempts);
        Assert.Null(chunk.ClaimedBy);
        Assert.Null(chunk.FireInstanceId);

        var failure = new GoldpathJobItemFailure();
        Assert.Equal("", failure.ItemKey);
        Assert.Equal("", failure.Reason);

        var execution = new GoldpathJobExecution();
        Assert.Equal("", execution.SchedulerName);
        Assert.Equal("", execution.JobName);
        Assert.Equal("", execution.InstanceName);
        Assert.Equal("", execution.Outcome);
        Assert.Null(execution.RunId);
        Assert.Null(execution.Error);
    }

    [Fact]
    public void The_quartz_store_tables_ride_the_same_model()
    {
        // The D2 promise: the store never escapes migration discipline — every qrtz table
        // is part of the EF model this context migrates.
        var quartzTables = Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(name => name?.StartsWith("qrtz_", StringComparison.Ordinal) == true)
            .ToList();
        Assert.Equal(11, quartzTables.Count);
        Assert.Contains("qrtz_job_details", quartzTables);
        Assert.Contains("qrtz_triggers", quartzTables);
        Assert.Contains("qrtz_fired_triggers", quartzTables);
        Assert.Contains("qrtz_scheduler_state", quartzTables);
        Assert.Contains("qrtz_locks", quartzTables);
    }
}
