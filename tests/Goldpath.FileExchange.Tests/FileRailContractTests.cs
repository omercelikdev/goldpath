using Goldpath;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Goldpath.FileExchange.Tests;

/// <summary>
/// The rail declaration's contract edges: what the builder bakes, what the engine
/// announces, and the ledger behaviors the planted-fault rig does not reach — a failing
/// HANDLER quarantines like a failing parse, a re-quarantine upserts the reason, a rail
/// with no header starts at line one, and an engine without a broker publishes nothing
/// and ingests everything.
/// </summary>
public class FileRailContractTests
{
    private sealed record Row(string Reference, decimal Amount);

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

    private static GoldpathFileExchangeOptions Rail(
        Func<Row, CancellationToken, Task>? handle = null,
        int header = 0,
        Func<Row, string?>? validateRow = null)
        => new GoldpathFileExchangeOptions().AddRail<Row>("wire", rail =>
        {
            if (header > 0) rail.Header(header);
            rail.ParseLine(line =>
            {
                var parts = line.Split(';');
                return new Row(parts[0], decimal.Parse(parts[1]));
            });
            if (validateRow is not null) rail.ValidateRow(validateRow);
            rail.Handle(handle ?? ((_, _) => Task.CompletedTask));
        });

    [Fact]
    public async Task A_throwing_handler_quarantines_the_row_with_its_words()
    {
        var options = Rail(handle: (row, _) => row.Reference == "B-2"
            ? throw new InvalidOperationException("core banking said no")
            : Task.CompletedTask);
        var engine = new GoldpathFileRailEngine(options, new GoldpathInMemoryFileLedger(), NullLogger<GoldpathFileRailEngine>.Instance);

        var result = await engine.ProcessAsync("wire", "w.csv", ["B-1;10", "B-2;20", "B-3;30"]);

        Assert.Equal(2, result.Processed);
        var quarantined = Assert.Single(result.Quarantined);
        Assert.Equal(2, quarantined.Line);
        Assert.Equal("handle: core banking said no", quarantined.Reason);
    }

    [Fact]
    public async Task A_rail_with_no_declared_header_starts_at_line_one()
    {
        var options = Rail();
        var events = new RecordingPublisher();
        var engine = new GoldpathFileRailEngine(options, new GoldpathInMemoryFileLedger(), NullLogger<GoldpathFileRailEngine>.Instance, events);

        var result = await engine.ProcessAsync("wire", "w.csv", ["B-1;10", "B-2;20"]);

        Assert.Equal(2, result.Processed);
        // The announcement counts DATA rows — with no header, every line is data.
        Assert.Contains(events.Published, e => e is GoldpathFileReceived { DataRows: 2 });
    }

    [Fact]
    public async Task No_broker_means_no_events_and_a_full_ingest_anyway()
    {
        // The publisher is OPTIONAL by signature: a host without messaging still rides
        // the rail — silence on the bus, not a refusal.
        var engine = new GoldpathFileRailEngine(Rail(), new GoldpathInMemoryFileLedger(), NullLogger<GoldpathFileRailEngine>.Instance, publisher: null);
        var result = await engine.ProcessAsync("wire", "w.csv", ["B-1;10"]);
        Assert.Equal(1, result.Processed);
    }

    [Fact]
    public async Task A_requarantined_row_carries_the_LATEST_reason()
    {
        var ledger = new GoldpathInMemoryFileLedger();
        await ledger.QuarantineAsync("wire", "w.csv", 3, "first reason");
        await ledger.QuarantineAsync("wire", "w.csv", 3, "second reason");
        await ledger.QuarantineAsync("wire", "w.csv", 1, "another row");

        var rows = await ledger.GetQuarantineAsync("wire", "w.csv");

        Assert.Equal(2, rows.Count);
        Assert.Equal([1, 3], rows.Select(r => r.Line));   // ordered by line, upserted by key
        Assert.Equal("second reason", rows.Single(r => r.Line == 3).Reason);
    }

    [Fact]
    public async Task The_quarantine_list_is_scoped_to_its_file()
    {
        var ledger = new GoldpathInMemoryFileLedger();
        await ledger.QuarantineAsync("wire", "a.csv", 1, "a's problem");
        await ledger.QuarantineAsync("wire", "b.csv", 1, "b's problem");

        var rows = await ledger.GetQuarantineAsync("wire", "a.csv");
        Assert.Equal("a's problem", Assert.Single(rows).Reason);
    }

    [Fact]
    public async Task A_rejected_file_announces_the_rejection_and_nothing_else()
    {
        var options = new GoldpathFileExchangeOptions().AddRail<Row>("wire", rail => rail
            .ValidateFile(_ => "trailer mismatch")
            .ParseLine(line => new Row(line, 1m))
            .Handle((_, _) => Task.CompletedTask));
        var events = new RecordingPublisher();
        var engine = new GoldpathFileRailEngine(options, new GoldpathInMemoryFileLedger(), NullLogger<GoldpathFileRailEngine>.Instance, events);

        var result = await engine.ProcessAsync("wire", "w.csv", ["B-1;10"]);

        Assert.Equal("trailer mismatch", result.FileRejectedReason);
        var only = Assert.Single(events.Published);
        Assert.Equal("trailer mismatch", Assert.IsType<GoldpathFileRejected>(only).Reason);
    }

    [Fact]
    public async Task Row_validation_wins_over_the_handler_the_handler_never_sees_a_bad_row()
    {
        var handled = new List<string>();
        var options = Rail(
            handle: (row, _) => { handled.Add(row.Reference); return Task.CompletedTask; },
            validateRow: row => row.Amount > 0 ? null : "non-positive");
        var engine = new GoldpathFileRailEngine(options, new GoldpathInMemoryFileLedger(), NullLogger<GoldpathFileRailEngine>.Instance);

        await engine.ProcessAsync("wire", "w.csv", ["B-1;10", "B-2;0"]);

        Assert.Equal(["B-1"], handled);
    }

    [Fact]
    public void Rail_names_are_case_insensitive_a_file_named_loudly_still_lands()
    {
        var options = Rail();
        Assert.True(options.Rails.ContainsKey("WIRE"));
    }
}
