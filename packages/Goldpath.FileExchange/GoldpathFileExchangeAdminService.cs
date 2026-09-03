namespace Goldpath;

/// <summary>One declared rail with its live counts — the console's landing view.</summary>
public sealed record GoldpathFileRailInfo(
    string Name,
    int HeaderLines,
    int FilesArchived,
    int QuarantineDepth,
    DateTimeOffset? LastArchivedAt);

/// <summary>One file's run outcome over the wire: what applied, what waits in quarantine.</summary>
public sealed record GoldpathFileInfo(
    string Rail,
    string File,
    int ProcessedRows,
    int QuarantinedRows,
    bool Archived,
    DateTimeOffset? ArchivedAt);

/// <summary>One quarantined row with its reason and age — what an operator triages.</summary>
public sealed record GoldpathFileQuarantineInfo(
    string Rail,
    string File,
    int Line,
    string Reason,
    DateTimeOffset QuarantinedAt);

/// <summary>
/// The ledger's READ side for the admin surface. Kept apart from <see cref="IGoldpathFileLedger"/>
/// on purpose: the engine's seam stays the six calls it shipped with, and an adopter ledger
/// that never composes the admin surface owes nothing more. Both shipped ledgers implement
/// it; a custom ledger without it makes <c>MapGoldpathFileExchangeAdmin</c> refuse at startup
/// with the teaching text, never a 500 at triage time.
/// </summary>
public interface IGoldpathFileLedgerQueries
{
    /// <summary>Recent files (archived or holding quarantine), newest archive first, optionally on one rail.</summary>
    Task<IReadOnlyList<GoldpathFileInfo>> ListFilesAsync(string? rail, int take, CancellationToken cancellationToken = default);

    /// <summary>The quarantine across files, oldest first, optionally on one rail — the depth the runbook watches.</summary>
    Task<IReadOnlyList<GoldpathFileQuarantineInfo>> ListQuarantineAsync(string? rail, int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// The file-exchange admin views (§7.1: the API is the contract). READ-ONLY on purpose:
/// a file arrives through the composed transport and its pick-up job — re-delivering it
/// IS the reprocess verb, and the engine dedups by construction — so a console that could
/// "reprocess" without the file would only pretend. What the operator gets here is the
/// rail's story: files in, rows applied, rows quarantined with their reasons and age.
/// </summary>
public sealed class GoldpathFileExchangeAdminService
{
    private readonly GoldpathFileExchangeOptions _options;
    private readonly IGoldpathFileLedgerQueries _queries;

    /// <summary>Registered by <c>AddGoldpathFileExchange</c>; the queries come from the composed ledger.</summary>
    public GoldpathFileExchangeAdminService(GoldpathFileExchangeOptions options, IGoldpathFileLedgerQueries queries)
    {
        _options = options;
        _queries = queries;
    }

    /// <summary>Every declared rail with its counts — the probe root, so an app without rails still answers.</summary>
    public async Task<IReadOnlyList<GoldpathFileRailInfo>> GetRailsAsync(CancellationToken cancellationToken)
    {
        // The counts come from the same take-bounded reads an operator gets — the contract
        // has no aggregate endpoint, and the console prints that scope rather than implying it.
        var files = await _queries.ListFilesAsync(null, AdminPaging.MaxTake, cancellationToken);
        var quarantine = await _queries.ListQuarantineAsync(null, AdminPaging.MaxTake, cancellationToken);
        var result = new List<GoldpathFileRailInfo>();
        foreach (var rail in _options.Rails.Values.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            var railFiles = files.Where(f => string.Equals(f.Rail, rail.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            result.Add(new GoldpathFileRailInfo(
                rail.Name,
                rail.HeaderLines,
                railFiles.Count(f => f.Archived),
                quarantine.Count(q => string.Equals(q.Rail, rail.Name, StringComparison.OrdinalIgnoreCase)),
                railFiles.Where(f => f.ArchivedAt is not null).Select(f => f.ArchivedAt).Max()));
        }

        return result;
    }

    /// <summary>Recent files, newest archive first; <c>?rail=</c> narrows (R3: repeats OR together).</summary>
    public async Task<IReadOnlyList<GoldpathFileInfo>> GetFilesAsync(string[]? rail, int take, CancellationToken cancellationToken)
    {
        var clamped = AdminPaging.Clamp(take);
        if (rail is { Length: 1 })
        {
            return await _queries.ListFilesAsync(rail[0], clamped, cancellationToken);
        }

        var files = await _queries.ListFilesAsync(null, rail is { Length: > 1 } ? AdminPaging.MaxTake : clamped, cancellationToken);
        if (rail is { Length: > 1 })
        {
            files = files.Where(f => rail.Contains(f.Rail, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        return files.Take(clamped).ToList();
    }

    /// <summary>The quarantine, oldest first; <c>?rail=</c> and <c>?file=</c> narrow (R3: values OR within, filters AND).</summary>
    public async Task<IReadOnlyList<GoldpathFileQuarantineInfo>> GetQuarantineAsync(string[]? rail, string[]? file, int take, CancellationToken cancellationToken)
    {
        var clamped = AdminPaging.Clamp(take);
        var filtered = rail is { Length: > 1 } || file is { Length: > 0 };
        var rows = await _queries.ListQuarantineAsync(rail is { Length: 1 } ? rail[0] : null, filtered ? AdminPaging.MaxTake : clamped, cancellationToken);
        IEnumerable<GoldpathFileQuarantineInfo> query = rows;
        if (rail is { Length: > 1 })
        {
            query = query.Where(q => rail.Contains(q.Rail, StringComparer.OrdinalIgnoreCase));
        }

        if (file is { Length: > 0 })
        {
            query = query.Where(q => file.Contains(q.File, StringComparer.OrdinalIgnoreCase));
        }

        return query.Take(clamped).ToList();
    }
}
