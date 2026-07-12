using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Goldpath.Bulk.Tests;

/// <summary>
/// The store schema is a WIRE CONTRACT (ops queries, provider DDL): golden assertions over
/// the EF model metadata — table names, keys, lengths, indexes, the concurrency token.
/// A mutated model literal is a real defect, not noise.
/// </summary>
public class ModelContractTests : IDisposable
{
    private readonly BulkFixture _fixture = new();
    private readonly IModel _model;

    public ModelContractTests()
        => _model = _fixture.Query(db => db.Model);

    public void Dispose() => _fixture.Dispose();

    private IEntityType Entity<T>() => _model.FindEntityType(typeof(T))!;

    [Fact]
    public void Tables_are_the_documented_names()
    {
        Assert.Equal("GoldpathBulkFiles", Entity<GoldpathBulkFile>().GetTableName());
        Assert.Equal("GoldpathBulkFileChunks", Entity<GoldpathBulkFileChunk>().GetTableName());
        Assert.Equal("GoldpathBulkBatches", Entity<GoldpathBulkBatch>().GetTableName());
        Assert.Equal("GoldpathBulkRows", Entity<GoldpathBulkRow>().GetTableName());
        Assert.Equal("GoldpathBulkRowErrors", Entity<GoldpathBulkRowError>().GetTableName());
    }

    [Fact]
    public void Keys_are_the_documented_shapes()
    {
        Assert.Equal(["Id"], Entity<GoldpathBulkFile>().FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(["FileId", "Index"], Entity<GoldpathBulkFileChunk>().FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(["Id"], Entity<GoldpathBulkBatch>().FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(["BatchId", "RowNumber"], Entity<GoldpathBulkRow>().FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(["Id"], Entity<GoldpathBulkRowError>().FindPrimaryKey()!.Properties.Select(p => p.Name));

        // Client-generated Guids: the engine mints ids, the store never does.
        Assert.Equal(ValueGenerated.Never, Entity<GoldpathBulkFile>().FindProperty("Id")!.ValueGenerated);
        Assert.Equal(ValueGenerated.Never, Entity<GoldpathBulkBatch>().FindProperty("Id")!.ValueGenerated);
    }

    [Fact]
    public void Bounded_lengths_serve_both_providers()
    {
        Assert.Equal(64, Entity<GoldpathBulkFile>().FindProperty("Sha256")!.GetMaxLength());
        Assert.Equal(260, Entity<GoldpathBulkFile>().FindProperty("FileName")!.GetMaxLength());
        Assert.Equal(128, Entity<GoldpathBulkBatch>().FindProperty("Definition")!.GetMaxLength());
        Assert.Equal(128, Entity<GoldpathBulkBatch>().FindProperty("Tenant")!.GetMaxLength());
        Assert.Equal(256, Entity<GoldpathBulkBatch>().FindProperty("DecidedBy")!.GetMaxLength());
        Assert.Equal(1024, Entity<GoldpathBulkBatch>().FindProperty("DecisionNote")!.GetMaxLength());
        Assert.Equal(128, Entity<GoldpathBulkRowError>().FindProperty("Field")!.GetMaxLength());
        Assert.Equal(512, Entity<GoldpathBulkRowError>().FindProperty("Message")!.GetMaxLength());
    }

    [Fact]
    public void Indexes_serve_the_hot_queries_and_the_dedup_identity()
    {
        var fileIndex = Assert.Single(Entity<GoldpathBulkFile>().GetIndexes());
        Assert.Equal(["Sha256"], fileIndex.Properties.Select(p => p.Name));
        Assert.True(fileIndex.IsUnique, "the content hash IS the dedup identity — uniqueness is the race guard");

        var batchIndexes = Entity<GoldpathBulkBatch>().GetIndexes().ToList();
        Assert.Contains(batchIndexes, i => i.Properties.Select(p => p.Name).SequenceEqual(["Definition", "State"]));
        Assert.Contains(batchIndexes, i => i.Properties.Select(p => p.Name).SequenceEqual(["FileId"]));

        var errorIndex = Assert.Single(Entity<GoldpathBulkRowError>().GetIndexes());
        Assert.Equal(["BatchId", "RowNumber"], errorIndex.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Batch_state_is_the_concurrency_token()
    {
        Assert.True(Entity<GoldpathBulkBatch>().FindProperty("State")!.IsConcurrencyToken,
            "state transitions must never race silently — adopt/complete rely on this");
    }

    [Fact]
    public void The_state_machine_enum_values_are_wire_stable()
    {
        Assert.Equal(0, (int)GoldpathBulkBatchState.Received);
        Assert.Equal(1, (int)GoldpathBulkBatchState.Validating);
        Assert.Equal(2, (int)GoldpathBulkBatchState.Validated);
        Assert.Equal(3, (int)GoldpathBulkBatchState.Approved);
        Assert.Equal(4, (int)GoldpathBulkBatchState.Rejected);
        Assert.Equal(5, (int)GoldpathBulkBatchState.Executing);
        Assert.Equal(6, (int)GoldpathBulkBatchState.Completed);
        Assert.Equal(7, (int)GoldpathBulkBatchState.CompletedWithFailures);
    }
}
