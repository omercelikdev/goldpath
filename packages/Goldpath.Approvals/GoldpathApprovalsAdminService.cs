namespace Goldpath;

/// <summary>One approval request over the admin wire — the list row the console renders.</summary>
public sealed record GoldpathApprovalRequestInfo(
    Guid Id,
    string Ladder,
    string Subject,
    decimal Amount,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    string PendingRole,
    DateTimeOffset PendingSince,
    string Status,
    string? DecidedBy,
    string? Reason,
    Guid? SupersedesId,
    int SignatureCount,
    int RequiredApprovals);

/// <summary>One request's full story: the row plus its trail and collected signatures.</summary>
public sealed record GoldpathApprovalRequestDetail(
    GoldpathApprovalRequestInfo Request,
    IReadOnlyList<GoldpathApprovalTrailEntry> Trail,
    IReadOnlyList<GoldpathApprovalSignature> Signatures);

/// <summary>
/// The approvals admin views (§7.1: the API is the contract). Reads ride the STORE seam —
/// the same one both shipped stores implement — so the surface works identically over the
/// in-memory and the database store; decisions go through the ENGINE (the endpoints call
/// it directly), because an admin verb that bypassed four-eyes would not be an admin verb,
/// it would be a hole.
/// </summary>
public sealed class GoldpathApprovalsAdminService
{
    private readonly IGoldpathApprovalStore _store;
    private readonly GoldpathApprovalsOptions _options;

    /// <summary>Registered by <c>AddGoldpathApprovals</c>.</summary>
    public GoldpathApprovalsAdminService(IGoldpathApprovalStore store, GoldpathApprovalsOptions options)
    {
        _store = store;
        _options = options;
    }

    /// <summary>
    /// Recent requests, newest first. Filters follow contract R3 (values OR within a
    /// filter, filters AND together) and apply over the recent WINDOW — the surface is a
    /// triage view, not a reporting query.
    /// </summary>
    public async Task<IReadOnlyList<GoldpathApprovalRequestInfo>> GetRequestsAsync(
        string[]? status, string[]? ladder, int take, CancellationToken cancellationToken)
    {
        var clamped = AdminPaging.Clamp(take);
        var filtered = status is { Length: > 0 } || ladder is { Length: > 0 };
        var window = await _store.GetRecentAsync(filtered ? AdminPaging.MaxTake : clamped, cancellationToken);
        IEnumerable<GoldpathApprovalRequest> rows = window;
        if (status is { Length: > 0 })
        {
            rows = rows.Where(r => status.Contains(r.Status.ToString(), StringComparer.OrdinalIgnoreCase));
        }

        if (ladder is { Length: > 0 })
        {
            rows = rows.Where(r => ladder.Contains(r.Ladder, StringComparer.OrdinalIgnoreCase));
        }

        var result = new List<GoldpathApprovalRequestInfo>();
        foreach (var request in rows.Take(clamped))
        {
            result.Add(await ToInfoAsync(request, cancellationToken));
        }

        return result;
    }

    /// <summary>One request's full story, or null.</summary>
    public async Task<GoldpathApprovalRequestDetail?> GetRequestAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await _store.GetAsync(id, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var signatures = await _store.GetSignaturesAsync(id, cancellationToken);
        return new GoldpathApprovalRequestDetail(await ToInfoAsync(request, cancellationToken), request.Trail, signatures);
    }

    private async Task<GoldpathApprovalRequestInfo> ToInfoAsync(GoldpathApprovalRequest request, CancellationToken cancellationToken)
    {
        var signatures = await _store.GetSignaturesAsync(request.Id, cancellationToken);
        return new GoldpathApprovalRequestInfo(
            request.Id, request.Ladder, request.Subject, request.Amount, request.RequestedBy,
            request.RequestedAt, request.PendingRole, request.PendingSince, request.Status.ToString(),
            request.DecidedBy, request.Reason, request.SupersedesId,
            signatures.Count(s => string.Equals(s.Role, request.PendingRole, StringComparison.OrdinalIgnoreCase)),
            RequiredApprovalsFor(request));
    }

    private int RequiredApprovalsFor(GoldpathApprovalRequest request)
    {
        if (!_options.Ladders.TryGetValue(request.Ladder, out var ladder))
        {
            return 1;
        }

        var rung = ladder.Rungs.FirstOrDefault(r => string.Equals(r.Role, request.PendingRole, StringComparison.OrdinalIgnoreCase));
        return rung?.RequiredApprovals ?? 1;
    }
}
