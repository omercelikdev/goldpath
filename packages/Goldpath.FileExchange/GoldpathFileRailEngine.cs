using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>Where rail progress lives: processed row keys, quarantine records, archive
/// marks. The in-memory ledger ships for tests and single-node hosts; a database-backed
/// ledger composes through this seam.</summary>
public interface IGoldpathFileLedger
{
    /// <summary>Whether a row already applied — the (rail, file, line) idempotency key.</summary>
    Task<bool> IsProcessedAsync(string rail, string file, int line, CancellationToken cancellationToken = default);

    /// <summary>Marks a row applied.</summary>
    Task MarkProcessedAsync(string rail, string file, int line, CancellationToken cancellationToken = default);

    /// <summary>Records a quarantined row with its reason (upserts on reprocess).</summary>
    Task QuarantineAsync(string rail, string file, int line, string reason, CancellationToken cancellationToken = default);

    /// <summary>Clears a row's quarantine record (it applied on reprocess).</summary>
    Task ReleaseQuarantineAsync(string rail, string file, int line, CancellationToken cancellationToken = default);

    /// <summary>The current quarantine list for a file.</summary>
    Task<IReadOnlyList<GoldpathQuarantinedRow>> GetQuarantineAsync(string rail, string file, CancellationToken cancellationToken = default);

    /// <summary>Marks the file archived (retention is the Archival module's business).</summary>
    Task MarkArchivedAsync(string rail, string file, CancellationToken cancellationToken = default);
}

/// <summary>One quarantined row: where and why.</summary>
public sealed record GoldpathQuarantinedRow(string Rail, string File, int Line, string Reason);

/// <summary>The outcome of one rail run over one file.</summary>
public sealed record GoldpathFileRailResult(
    string Rail,
    string File,
    string? FileRejectedReason,
    int Processed,
    int SkippedAsDuplicate,
    IReadOnlyList<GoldpathQuarantinedRow> Quarantined);

/// <summary>A file arrived on a rail.</summary>
public sealed record GoldpathFileReceived(string Rail, string File, int DataRows) : IIntegrationEvent;

/// <summary>A rail run finished ingesting a file.</summary>
public sealed record GoldpathFileIngested(string Rail, string File, int Processed, int SkippedAsDuplicate, int Quarantined) : IIntegrationEvent;

/// <summary>A rail run quarantined rows — the batch CONTINUED (that is the point).</summary>
public sealed record GoldpathRowsQuarantined(string Rail, string File, int Count) : IIntegrationEvent;

/// <summary>A file failed its file-level contract and ingested NOTHING.</summary>
public sealed record GoldpathFileRejected(string Rail, string File, string Reason) : IIntegrationEvent;

/// <summary>
/// The rail engine: validate the file, then per row — dedup on (rail, file, line),
/// parse/validate/handle, quarantine on failure WITHOUT stopping the batch. Re-running
/// the same file (redelivery or reprocess) applies zero duplicates and retries only what
/// quarantined. Scheduled pick-up rides the Jobs module; this engine owns one run.
/// </summary>
public sealed class GoldpathFileRailEngine
{
    private readonly GoldpathFileExchangeOptions _options;
    private readonly IGoldpathFileLedger _ledger;
    private readonly ILogger<GoldpathFileRailEngine> _logger;
    private readonly IIntegrationEventPublisher? _publisher;

    /// <summary>Creates the engine (the publisher is optional — no broker, no events).</summary>
    public GoldpathFileRailEngine(
        GoldpathFileExchangeOptions options,
        IGoldpathFileLedger ledger,
        ILogger<GoldpathFileRailEngine> logger,
        IIntegrationEventPublisher? publisher = null)
    {
        _options = options;
        _ledger = ledger;
        _logger = logger;
        _publisher = publisher;
    }

    /// <summary>Runs one file through its rail. Idempotent per (rail, file, line).</summary>
    public async Task<GoldpathFileRailResult> ProcessAsync(string railName, string file, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        if (!_options.Rails.TryGetValue(railName, out var rail))
        {
            throw new InvalidOperationException($"Rail '{railName}' is not declared — files ingest on declared rails only.");
        }

        if (rail.ValidateFileCore(lines) is { } fileReason)
        {
            _logger.LogWarning("Rail {Rail} rejected file {File}: {Reason}", rail.Name, file, fileReason);
            await PublishAsync(new GoldpathFileRejected(rail.Name, file, fileReason), cancellationToken);
            return new GoldpathFileRailResult(rail.Name, file, fileReason, 0, 0, []);
        }

        var dataRows = lines.Count - rail.HeaderLines;
        await PublishAsync(new GoldpathFileReceived(rail.Name, file, dataRows), cancellationToken);

        var processed = 0;
        var skipped = 0;
        var quarantined = new List<GoldpathQuarantinedRow>();
        for (var i = rail.HeaderLines; i < lines.Count; i++)
        {
            var lineNo = i + 1;   // 1-based, matches what operators see in the file
            if (await _ledger.IsProcessedAsync(rail.Name, file, lineNo, cancellationToken))
            {
                skipped++;
                continue;
            }

            if (await rail.ProcessRowCore(lines[i], cancellationToken) is { } reason)
            {
                await _ledger.QuarantineAsync(rail.Name, file, lineNo, reason, cancellationToken);
                quarantined.Add(new GoldpathQuarantinedRow(rail.Name, file, lineNo, reason));
                continue;   // the batch does NOT stop — quarantine is per row
            }

            await _ledger.MarkProcessedAsync(rail.Name, file, lineNo, cancellationToken);
            await _ledger.ReleaseQuarantineAsync(rail.Name, file, lineNo, cancellationToken);
            processed++;
        }

        if (quarantined.Count > 0)
        {
            await PublishAsync(new GoldpathRowsQuarantined(rail.Name, file, quarantined.Count), cancellationToken);
        }

        await _ledger.MarkArchivedAsync(rail.Name, file, cancellationToken);
        await PublishAsync(new GoldpathFileIngested(rail.Name, file, processed, skipped, quarantined.Count), cancellationToken);
        return new GoldpathFileRailResult(rail.Name, file, null, processed, skipped, quarantined);
    }

    private Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent
        => _publisher?.PublishAsync(integrationEvent, cancellationToken) ?? Task.CompletedTask;
}

/// <summary>In-memory ledger: tests and single-node hosts; database ledgers compose via the seam.</summary>
public sealed class GoldpathInMemoryFileLedger : IGoldpathFileLedger
{
    private readonly object _gate = new();
    private readonly HashSet<(string Rail, string File, int Line)> _processed = [];
    private readonly Dictionary<(string Rail, string File, int Line), string> _quarantine = [];
    private readonly HashSet<(string Rail, string File)> _archived = [];

    /// <inheritdoc />
    public Task<bool> IsProcessedAsync(string rail, string file, int line, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_processed.Contains((rail, file, line)));
        }
    }

    /// <inheritdoc />
    public Task MarkProcessedAsync(string rail, string file, int line, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _processed.Add((rail, file, line));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QuarantineAsync(string rail, string file, int line, string reason, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _quarantine[(rail, file, line)] = reason;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseQuarantineAsync(string rail, string file, int line, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _quarantine.Remove((rail, file, line));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GoldpathQuarantinedRow>> GetQuarantineAsync(string rail, string file, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<GoldpathQuarantinedRow> rows = _quarantine
                .Where(kv => kv.Key.Rail == rail && kv.Key.File == file)
                .OrderBy(kv => kv.Key.Line)
                .Select(kv => new GoldpathQuarantinedRow(kv.Key.Rail, kv.Key.File, kv.Key.Line, kv.Value))
                .ToList();
            return Task.FromResult(rows);
        }
    }

    /// <inheritdoc />
    public Task MarkArchivedAsync(string rail, string file, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _archived.Add((rail, file));
        }

        return Task.CompletedTask;
    }
}
