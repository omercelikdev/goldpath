namespace Goldpath;

/// <summary>Where an approval request stands in its lifecycle.</summary>
public enum GoldpathApprovalStatus
{
    /// <summary>Waiting for the current rung's decision.</summary>
    Pending,

    /// <summary>Granted — the guarded action may proceed.</summary>
    Granted,

    /// <summary>Rejected — the guarded action must not proceed.</summary>
    Rejected,

    /// <summary>The top rung's deadline passed without a decision.</summary>
    Expired,
}

/// <summary>One approval request and its full decision trail.</summary>
public sealed class GoldpathApprovalRequest
{
    /// <summary>Request identity.</summary>
    public Guid Id { get; init; }

    /// <summary>The ladder this request runs.</summary>
    public required string Ladder { get; init; }

    /// <summary>What is being approved (an adopter-meaningful subject key).</summary>
    public required string Subject { get; init; }

    /// <summary>The amount that routed the rung.</summary>
    public decimal Amount { get; init; }

    /// <summary>Who asked — the four-eyes rule bars this identity from deciding.</summary>
    public required string RequestedBy { get; init; }

    /// <summary>When the request was made.</summary>
    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>The role currently expected to decide.</summary>
    public required string PendingRole { get; set; }

    /// <summary>When the current rung took the request (deadlines count from here).</summary>
    public DateTimeOffset PendingSince { get; set; }

    /// <summary>Lifecycle status.</summary>
    public GoldpathApprovalStatus Status { get; set; }

    /// <summary>Who decided, when terminal.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Decision reason, when terminal.</summary>
    public string? Reason { get; set; }

    /// <summary>Every lifecycle step, oldest first — the audit value e-mail never had.</summary>
    public List<GoldpathApprovalTrailEntry> Trail { get; } = [];
}

/// <summary>One audited lifecycle step.</summary>
public sealed record GoldpathApprovalTrailEntry(DateTimeOffset At, string Actor, string Action, string Detail);

/// <summary>An active delegation: <c>From</c>'s pending items may be decided by <c>To</c>.</summary>
public sealed record GoldpathApprovalDelegation(string From, string To, DateTimeOffset Until);

/// <summary>An approval was requested and routed to a rung.</summary>
public sealed record GoldpathApprovalRequested(Guid ApprovalId, string Ladder, string Subject, decimal Amount, string PendingRole) : IIntegrationEvent;

/// <summary>An approval was granted — the guarded action may proceed.</summary>
public sealed record GoldpathApprovalGranted(Guid ApprovalId, string Ladder, string Subject, string DecidedBy) : IIntegrationEvent;

/// <summary>An approval was rejected.</summary>
public sealed record GoldpathApprovalRejected(Guid ApprovalId, string Ladder, string Subject, string DecidedBy, string Reason) : IIntegrationEvent;

/// <summary>A pending approval escalated one rung on deadline.</summary>
public sealed record GoldpathApprovalEscalated(Guid ApprovalId, string Ladder, string Subject, string FromRole, string ToRole) : IIntegrationEvent;

/// <summary>A pending approval expired at the top rung.</summary>
public sealed record GoldpathApprovalExpired(Guid ApprovalId, string Ladder, string Subject, string AtRole) : IIntegrationEvent;
