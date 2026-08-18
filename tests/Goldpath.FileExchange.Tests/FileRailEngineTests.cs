using Goldpath;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Goldpath.FileExchange.Tests;

/// <summary>
/// The planted-fault rig (fileexchange RFC §7): a registry-style CSV rail with a header,
/// a trailer count, and rows "reference;amount". Every planted fault — bad row, duplicate
/// file, truncated file, replay — must be caught, quarantined, or deduplicated exactly as
/// declared, and the batch must never stop for a row.
/// </summary>
public class FileRailEngineTests
{
    private sealed record RegistryRow(string Reference, decimal Amount);

    private static GoldpathFileExchangeOptions Options() => new GoldpathFileExchangeOptions()
        .AddRail<RegistryRow>("registry-daily", rail => rail
            .Header(1)
            .ValidateFile(lines =>
            {
                // Trailer contract: the header's declared row count must match reality.
                if (lines.Count == 0 || !lines[0].StartsWith("H;", StringComparison.Ordinal))
                {
                    return "missing header";
                }

                var declared = int.Parse(lines[0].Split(';')[1]);
                return declared == lines.Count - 1 ? null : $"truncated: header declares {declared} rows, file has {lines.Count - 1}";
            })
            .ParseLine(line =>
            {
                var parts = line.Split(';');
                return new RegistryRow(parts[0], decimal.Parse(parts[1]));
            })
            .ValidateRow(row => row.Amount > 0 ? null : $"non-positive amount for {row.Reference}")
            .Handle((row, _) =>
            {
                Applied.Add(row.Reference);
                return Task.CompletedTask;
            }));

    private static readonly List<string> Applied = [];

    private static (GoldpathFileRailEngine Engine, GoldpathInMemoryFileLedger Ledger, RecordingPublisher Events) Build()
    {
        Applied.Clear();
        var ledger = new GoldpathInMemoryFileLedger();
        var events = new RecordingPublisher();
        var engine = new GoldpathFileRailEngine(Options(), ledger, NullLogger<GoldpathFileRailEngine>.Instance, events);
        return (engine, ledger, events);
    }

    private static readonly string[] CleanFile = ["H;3", "A-1;100.50", "A-2;200.00", "A-3;50.25"];

    [Fact]
    public async Task Clean_file_ingests_every_row_once()
    {
        var (engine, _, events) = Build();
        var result = await engine.ProcessAsync("registry-daily", "reg-0817.csv", CleanFile);

        Assert.Null(result.FileRejectedReason);
        Assert.Equal(3, result.Processed);
        Assert.Empty(result.Quarantined);
        Assert.Equal(["A-1", "A-2", "A-3"], Applied);
        Assert.Contains(events.Published, e => e is GoldpathFileIngested { Processed: 3, Quarantined: 0 });
    }

    [Fact]
    public async Task Bad_rows_quarantine_and_the_batch_continues()
    {
        var (engine, ledger, events) = Build();
        string[] file = ["H;4", "A-1;100.50", "garbage-line", "A-3;-5", "A-4;70.00"];
        var result = await engine.ProcessAsync("registry-daily", "reg-0818.csv", file);

        Assert.Equal(2, result.Processed);                       // the batch did NOT stop
        Assert.Equal([3, 4], result.Quarantined.Select(q => q.Line));
        Assert.StartsWith("parse:", result.Quarantined[0].Reason);
        Assert.Equal("non-positive amount for A-3", result.Quarantined[1].Reason);
        Assert.Equal(["A-1", "A-4"], Applied);

        var quarantine = await ledger.GetQuarantineAsync("registry-daily", "reg-0818.csv");
        Assert.Equal(2, quarantine.Count);
        Assert.Contains(events.Published, e => e is GoldpathRowsQuarantined { Count: 2 });
    }

    [Fact]
    public async Task Replaying_the_same_file_applies_zero_duplicates()
    {
        var (engine, _, _) = Build();
        await engine.ProcessAsync("registry-daily", "reg-0817.csv", CleanFile);
        var replay = await engine.ProcessAsync("registry-daily", "reg-0817.csv", CleanFile);

        Assert.Equal(0, replay.Processed);
        Assert.Equal(3, replay.SkippedAsDuplicate);
        Assert.Equal(["A-1", "A-2", "A-3"], Applied);   // still exactly once
    }

    [Fact]
    public async Task Truncated_file_is_rejected_whole_and_ingests_nothing()
    {
        var (engine, _, events) = Build();
        string[] truncated = ["H;3", "A-1;100.50", "A-2;200.00"];   // header says 3, file has 2
        var result = await engine.ProcessAsync("registry-daily", "reg-0819.csv", truncated);

        Assert.NotNull(result.FileRejectedReason);
        Assert.Equal(0, result.Processed);
        Assert.Empty(Applied);
        Assert.Contains(events.Published, e => e is GoldpathFileRejected);
        Assert.DoesNotContain(events.Published, e => e is GoldpathFileIngested);
    }

    [Fact]
    public async Task Reprocess_after_fix_retries_only_the_quarantined_row()
    {
        var (engine, ledger, _) = Build();
        string[] broken = ["H;2", "A-1;100.50", "A-2;-1"];
        await engine.ProcessAsync("registry-daily", "reg-0820.csv", broken);
        Assert.Equal(["A-1"], Applied);

        // The counterparty resends the file with the row fixed. The good row dedups,
        // the fixed row applies, and its quarantine record clears.
        string[] fixedFile = ["H;2", "A-1;100.50", "A-2;75.00"];
        var result = await engine.ProcessAsync("registry-daily", "reg-0820.csv", fixedFile);

        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.SkippedAsDuplicate);
        Assert.Equal(["A-1", "A-2"], Applied);
        Assert.Empty(await ledger.GetQuarantineAsync("registry-daily", "reg-0820.csv"));
    }

    [Fact]
    public async Task An_undeclared_rail_is_refused()
    {
        var (engine, _, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.ProcessAsync("no-such-rail", "x.csv", CleanFile));
    }

    [Fact]
    public void A_rail_without_parse_or_handle_is_rejected_at_declaration()
    {
        Assert.Throws<InvalidOperationException>(() => new GoldpathFileExchangeOptions()
            .AddRail<RegistryRow>("broken", rail => rail.Header(1)));
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<IIntegrationEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : class, IIntegrationEvent
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
