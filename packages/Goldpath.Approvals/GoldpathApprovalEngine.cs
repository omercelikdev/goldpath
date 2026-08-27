using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>Where approval state lives. The module ships an in-memory store for tests and
/// single-node hosts; a database-backed store composes through this seam.</summary>
public interface IGoldpathApprovalStore
{
    /// <summary>Adds a new request.</summary>
    Task AddAsync(GoldpathApprovalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Loads one request, or null.</summary>
    Task<GoldpathApprovalRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Persists a mutated request.</summary>
    Task UpdateAsync(GoldpathApprovalRequest request, CancellationToken cancellationToken = default);

    /// <summary>All PENDING requests (the worklist and the escalation sweep read this).</summary>
    Task<IReadOnlyList<GoldpathApprovalRequest>> GetPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a delegation.</summary>
    Task AddDelegationAsync(GoldpathApprovalDelegation delegation, CancellationToken cancellationToken = default);

    /// <summary>Active delegations (unexpired).</summary>
    Task<IReadOnlyList<GoldpathApprovalDelegation>> GetDelegationsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Adds one collected grant toward a rung's quorum.</summary>
    Task AddSignatureAsync(GoldpathApprovalSignature signature, CancellationToken cancellationToken = default);

    /// <summary>All signatures collected on one request, oldest first.</summary>
    Task<IReadOnlyList<GoldpathApprovalSignature>> GetSignaturesAsync(Guid requestId, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a decision attempt — refusals are values, not exceptions.</summary>
public enum GoldpathApprovalDecisionOutcome
{
    /// <summary>The decision was applied.</summary>
    Applied,

    /// <summary>Unknown request id.</summary>
    NotFound,

    /// <summary>The request is not pending.</summary>
    NotPending,

    /// <summary>Four-eyes: the requester may never decide their own request.</summary>
    FourEyesViolation,

    /// <summary>The decider does not hold the pending rung's role (nor a valid delegation).</summary>
    WrongRole,

    /// <summary>Distinct-eyes: this identity already signed this request — one signature per person, across rungs.</summary>
    AlreadySigned,

    /// <summary>Rejection without a reason teaches the requester nothing; the reason is mandatory.</summary>
    ReasonRequired,

    /// <summary>Only the requester may withdraw their own pending request.</summary>
    NotRequester,
}

/// <summary>
/// The approvals engine: routes by amount, enforces four-eyes, applies decisions,
/// honors bounded delegation, and escalates on rung deadlines. Every step lands in the
/// request's trail — the audit value the e-mail thread never had.
/// </summary>
public sealed class GoldpathApprovalEngine
{
    private readonly GoldpathApprovalsOptions _options;
    private readonly IGoldpathApprovalStore _store;
    private readonly TimeProvider _time;
    private readonly IIntegrationEventPublisher? _publisher;
    private readonly ILogger<GoldpathApprovalEngine> _logger;

    /// <summary>Creates the engine (the publisher is optional — no broker, no events).</summary>
    public GoldpathApprovalEngine(
        GoldpathApprovalsOptions options,
        IGoldpathApprovalStore store,
        TimeProvider time,
        ILogger<GoldpathApprovalEngine> logger,
        IIntegrationEventPublisher? publisher = null)
    {
        _options = options;
        _store = store;
        _time = time;
        _logger = logger;
        _publisher = publisher;
    }

    /// <summary>Requests an approval; the amount routes the rung.</summary>
    public async Task<GoldpathApprovalRequest> RequestAsync(string ladderName, string subject, decimal amount, string requestedBy, CancellationToken cancellationToken = default)
    {
        if (!_options.Ladders.TryGetValue(ladderName, out var ladder))
        {
            throw new InvalidOperationException($"Ladder '{ladderName}' is not declared — approvals run on declared ladders only.");
        }

        var now = _time.GetUtcNow();
        var rung = ladder.Route(amount);
        var request = new GoldpathApprovalRequest
        {
            Id = Guid.NewGuid(),
            Ladder = ladder.Name,
            Subject = subject,
            Amount = amount,
            RequestedBy = requestedBy,
            RequestedAt = now,
            PendingRole = rung.Role,
            PendingSince = now,
            Status = GoldpathApprovalStatus.Pending,
        };
        request.Trail.Add(new GoldpathApprovalTrailEntry(now, requestedBy, "requested", $"routed to {rung.Role} for {amount}"));
        await _store.AddAsync(request, cancellationToken);
        await PublishAsync(new GoldpathApprovalRequested(request.Id, ladder.Name, subject, amount, rung.Role), cancellationToken);
        return request;
    }

    /// <summary>
    /// Applies a grant/reject decision under four-eyes, role and distinct-eyes checks. A
    /// grant on a quorum rung collects a SIGNATURE; the request completes when the rung's
    /// quorum is met. A rejection is terminal at any point and its reason is mandatory.
    /// </summary>
    public async Task<GoldpathApprovalDecisionOutcome> DecideAsync(Guid id, string decidedBy, string deciderRole, bool granted, string reason, CancellationToken cancellationToken = default)
    {
        var request = await _store.GetAsync(id, cancellationToken);
        if (request is null)
        {
            return GoldpathApprovalDecisionOutcome.NotFound;
        }

        if (request.Status != GoldpathApprovalStatus.Pending)
        {
            return GoldpathApprovalDecisionOutcome.NotPending;
        }

        if (string.Equals(request.RequestedBy, decidedBy, StringComparison.OrdinalIgnoreCase))
        {
            return GoldpathApprovalDecisionOutcome.FourEyesViolation;
        }

        var now = _time.GetUtcNow();
        if (!string.Equals(request.PendingRole, deciderRole, StringComparison.OrdinalIgnoreCase)
            && !await HasDelegationToRoleAsync(decidedBy, request.PendingRole, now, cancellationToken))
        {
            return GoldpathApprovalDecisionOutcome.WrongRole;
        }

        if (!granted)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return GoldpathApprovalDecisionOutcome.ReasonRequired;
            }

            request.Status = GoldpathApprovalStatus.Rejected;
            request.DecidedBy = decidedBy;
            request.Reason = reason;
            request.Trail.Add(new GoldpathApprovalTrailEntry(now, decidedBy, "rejected", reason));
            await _store.UpdateAsync(request, cancellationToken);
            await PublishAsync(new GoldpathApprovalRejected(request.Id, request.Ladder, request.Subject, decidedBy, reason), cancellationToken);
            return GoldpathApprovalDecisionOutcome.Applied;
        }

        // Distinct-eyes reads the WHOLE chain: an identity that signed a lower rung may not
        // sign again after escalation, even holding the higher role — otherwise a two-step
        // chain is one person clicking twice.
        var signatures = await _store.GetSignaturesAsync(request.Id, cancellationToken);
        if (signatures.Any(s => string.Equals(s.SignedBy, decidedBy, StringComparison.OrdinalIgnoreCase)))
        {
            return GoldpathApprovalDecisionOutcome.AlreadySigned;
        }

        await _store.AddSignatureAsync(new GoldpathApprovalSignature(request.Id, decidedBy, request.PendingRole, now), cancellationToken);
        // Quorum counts per RUNG (signatures carry the rung they covered); an escalated
        // request starts its new rung's count from zero while the chain-wide list above
        // keeps every earlier signer barred.
        var collected = signatures.Count(s => string.Equals(s.Role, request.PendingRole, StringComparison.OrdinalIgnoreCase)) + 1;
        var required = RequiredApprovalsFor(request);
        if (collected < required)
        {
            request.Trail.Add(new GoldpathApprovalTrailEntry(now, decidedBy, "signed", $"{collected}/{required} at {request.PendingRole}: {reason}"));
            await _store.UpdateAsync(request, cancellationToken);
            return GoldpathApprovalDecisionOutcome.Applied;
        }

        request.Status = GoldpathApprovalStatus.Granted;
        request.DecidedBy = decidedBy;
        request.Reason = reason;
        request.Trail.Add(new GoldpathApprovalTrailEntry(now, decidedBy, "granted", reason));
        await _store.UpdateAsync(request, cancellationToken);
        await PublishAsync(new GoldpathApprovalGranted(request.Id, request.Ladder, request.Subject, decidedBy), cancellationToken);
        return GoldpathApprovalDecisionOutcome.Applied;
    }

    /// <summary>Takes back a PENDING request — only its requester may; the step lands in the trail.</summary>
    public async Task<GoldpathApprovalDecisionOutcome> WithdrawAsync(Guid id, string requestedBy, CancellationToken cancellationToken = default)
    {
        var request = await _store.GetAsync(id, cancellationToken);
        if (request is null)
        {
            return GoldpathApprovalDecisionOutcome.NotFound;
        }

        if (request.Status != GoldpathApprovalStatus.Pending)
        {
            return GoldpathApprovalDecisionOutcome.NotPending;
        }

        if (!string.Equals(request.RequestedBy, requestedBy, StringComparison.OrdinalIgnoreCase))
        {
            return GoldpathApprovalDecisionOutcome.NotRequester;
        }

        var now = _time.GetUtcNow();
        request.Status = GoldpathApprovalStatus.Withdrawn;
        request.Trail.Add(new GoldpathApprovalTrailEntry(now, requestedBy, "withdrawn", "taken back by the requester"));
        await _store.UpdateAsync(request, cancellationToken);
        return GoldpathApprovalDecisionOutcome.Applied;
    }

    /// <summary>
    /// Resubmits a REJECTED request as a fresh one: re-routed by the same amount, linked by
    /// <see cref="GoldpathApprovalRequest.SupersedesId"/>, both trails cross-referenced — the
    /// audit reads request → reject → rework as one story. Anything but a rejected request
    /// throws: resubmission is rework, not a retry channel.
    /// </summary>
    public async Task<GoldpathApprovalRequest> ResubmitAsync(Guid rejectedId, string requestedBy, CancellationToken cancellationToken = default)
    {
        var rejected = await _store.GetAsync(rejectedId, cancellationToken);
        if (rejected is null)
        {
            throw new InvalidOperationException($"Request '{rejectedId}' does not exist — only a rejected request may be resubmitted.");
        }

        if (rejected.Status != GoldpathApprovalStatus.Rejected)
        {
            throw new InvalidOperationException($"Request '{rejectedId}' is {rejected.Status} — only a rejected request may be resubmitted.");
        }

        if (!_options.Ladders.TryGetValue(rejected.Ladder, out var ladder))
        {
            throw new InvalidOperationException($"Ladder '{rejected.Ladder}' is not declared — approvals run on declared ladders only.");
        }

        var now = _time.GetUtcNow();
        var rung = ladder.Route(rejected.Amount);
        var request = new GoldpathApprovalRequest
        {
            Id = Guid.NewGuid(),
            Ladder = ladder.Name,
            Subject = rejected.Subject,
            Amount = rejected.Amount,
            RequestedBy = requestedBy,
            RequestedAt = now,
            PendingRole = rung.Role,
            PendingSince = now,
            Status = GoldpathApprovalStatus.Pending,
            SupersedesId = rejected.Id,
        };
        request.Trail.Add(new GoldpathApprovalTrailEntry(now, requestedBy, "resubmitted", $"supersedes {rejected.Id}; routed to {rung.Role} for {rejected.Amount}"));
        await _store.AddAsync(request, cancellationToken);

        rejected.Trail.Add(new GoldpathApprovalTrailEntry(now, requestedBy, "superseded", $"resubmitted as {request.Id}"));
        await _store.UpdateAsync(rejected, cancellationToken);

        await PublishAsync(new GoldpathApprovalRequested(request.Id, ladder.Name, request.Subject, request.Amount, rung.Role), cancellationToken);
        return request;
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

    /// <summary>
    /// Delegates <paramref name="from"/>'s pending decisions to <paramref name="to"/> for a
    /// bounded window. Depth is one: a delegate cannot re-delegate — the cycle guard is
    /// structural, not a graph search.
    /// </summary>
    public async Task DelegateAsync(string from, string to, TimeSpan window, CancellationToken cancellationToken = default)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Delegation to self is meaningless.");
        }

        if (window > _options.MaxDelegationWindow)
        {
            throw new InvalidOperationException($"Delegation window exceeds the declared maximum ({_options.MaxDelegationWindow}).");
        }

        var now = _time.GetUtcNow();
        var active = await _store.GetDelegationsAsync(now, cancellationToken);
        if (active.Any(d => string.Equals(d.To, from, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"'{from}' currently holds a delegation and cannot re-delegate (depth is one).");
        }

        await _store.AddDelegationAsync(new GoldpathApprovalDelegation(from, to, now + window), cancellationToken);
    }

    /// <summary>
    /// The escalation sweep: pending requests past their rung's deadline move UP one rung;
    /// overdue at the top rung EXPIRES. Schedule this through the Jobs module.
    /// </summary>
    public async Task<int> EscalateOverdueAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var moved = 0;
        foreach (var request in await _store.GetPendingAsync(cancellationToken))
        {
            if (!_options.Ladders.TryGetValue(request.Ladder, out var ladder))
            {
                continue;
            }

            var rung = ladder.Rungs.FirstOrDefault(r => string.Equals(r.Role, request.PendingRole, StringComparison.OrdinalIgnoreCase));
            if (rung is null || now - request.PendingSince < rung.EscalateAfter)
            {
                continue;
            }

            var above = ladder.Above(rung);
            if (above is null)
            {
                request.Status = GoldpathApprovalStatus.Expired;
                request.Trail.Add(new GoldpathApprovalTrailEntry(now, "system", "expired", $"overdue at top rung {rung.Role}"));
                await _store.UpdateAsync(request, cancellationToken);
                await PublishAsync(new GoldpathApprovalExpired(request.Id, request.Ladder, request.Subject, rung.Role), cancellationToken);
            }
            else
            {
                request.PendingRole = above.Role;
                request.PendingSince = now;
                request.Trail.Add(new GoldpathApprovalTrailEntry(now, "system", "escalated", $"{rung.Role} -> {above.Role}"));
                await _store.UpdateAsync(request, cancellationToken);
                await PublishAsync(new GoldpathApprovalEscalated(request.Id, request.Ladder, request.Subject, rung.Role, above.Role), cancellationToken);
            }

            moved++;
        }

        if (moved > 0)
        {
            _logger.LogInformation("Approvals escalation sweep moved {Count} request(s).", moved);
        }

        return moved;
    }

    /// <summary>
    /// The worklist: pending requests the given identity (holding the given role) may decide —
    /// their role's rung plus anything delegated to them. Four-eyes filters their own requests out.
    /// </summary>
    public async Task<IReadOnlyList<GoldpathApprovalRequest>> WorklistAsync(string identity, string role, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var delegations = await _store.GetDelegationsAsync(now, cancellationToken);
        var pending = await _store.GetPendingAsync(cancellationToken);
        return pending
            .Where(r => !string.Equals(r.RequestedBy, identity, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.Equals(r.PendingRole, role, StringComparison.OrdinalIgnoreCase)
                || delegations.Any(d => string.Equals(d.To, identity, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.PendingSince)
            .ToList();
    }

    private async Task<bool> HasDelegationToRoleAsync(string identity, string pendingRole, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A delegation transfers the DELEGATOR's decision right; v0 models identity-level
        // delegation, so holding any active delegation admits the delegate to the rung.
        var delegations = await _store.GetDelegationsAsync(now, cancellationToken);
        return delegations.Any(d => string.Equals(d.To, identity, StringComparison.OrdinalIgnoreCase));
    }

    private Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent
        => _publisher?.PublishAsync(integrationEvent, cancellationToken) ?? Task.CompletedTask;
}

/// <summary>In-memory store: tests and single-node hosts; database stores compose via the seam.</summary>
public sealed class GoldpathInMemoryApprovalStore : IGoldpathApprovalStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, GoldpathApprovalRequest> _requests = [];
    private readonly List<GoldpathApprovalDelegation> _delegations = [];
    private readonly List<GoldpathApprovalSignature> _signatures = [];

    /// <inheritdoc />
    public Task AddAsync(GoldpathApprovalRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _requests.Add(request.Id, request);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<GoldpathApprovalRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_requests.TryGetValue(id, out var request) ? request : null);
        }
    }

    /// <inheritdoc />
    public Task UpdateAsync(GoldpathApprovalRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _requests[request.Id] = request;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GoldpathApprovalRequest>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<GoldpathApprovalRequest> pending =
                _requests.Values.Where(r => r.Status == GoldpathApprovalStatus.Pending).ToList();
            return Task.FromResult(pending);
        }
    }

    /// <inheritdoc />
    public Task AddDelegationAsync(GoldpathApprovalDelegation delegation, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _delegations.Add(delegation);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GoldpathApprovalDelegation>> GetDelegationsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<GoldpathApprovalDelegation> active = _delegations.Where(d => d.Until > now).ToList();
            return Task.FromResult(active);
        }
    }

    /// <inheritdoc />
    public Task AddSignatureAsync(GoldpathApprovalSignature signature, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _signatures.Add(signature);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GoldpathApprovalSignature>> GetSignaturesAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<GoldpathApprovalSignature> signatures =
                _signatures.Where(s => s.RequestId == requestId).OrderBy(s => s.At).ToList();
            return Task.FromResult(signatures);
        }
    }
}
