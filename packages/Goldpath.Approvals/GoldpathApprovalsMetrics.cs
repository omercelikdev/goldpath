using System.Diagnostics.Metrics;

namespace Goldpath;

/// <summary>
/// The module's meter ("Goldpath.Approvals" — the ServiceDefaults wildcard exports it):
/// one counter per lifecycle step, tagged by ladder, so the ops dashboard reads the SAME
/// story the trail tells — volume in, decisions out, escalations and expiries as the
/// early-warning pair.
/// </summary>
public static class GoldpathApprovalsMetrics
{
    private static readonly Meter Meter = new("Goldpath.Approvals");
    private static readonly Counter<long> Requested = Meter.CreateCounter<long>("goldpath_approvals_requested_total", description: "Approval requests routed to a rung.");
    private static readonly Counter<long> Granted = Meter.CreateCounter<long>("goldpath_approvals_granted_total", description: "Requests granted (quorum complete).");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("goldpath_approvals_rejected_total", description: "Requests rejected (terminal, with reason).");
    private static readonly Counter<long> Escalated = Meter.CreateCounter<long>("goldpath_approvals_escalated_total", description: "Overdue requests moved one rung up.");
    private static readonly Counter<long> Expired = Meter.CreateCounter<long>("goldpath_approvals_expired_total", description: "Requests overdue at the top rung.");
    private static readonly Counter<long> Withdrawn = Meter.CreateCounter<long>("goldpath_approvals_withdrawn_total", description: "Requests taken back by their requester.");

    internal static void CountRequested(string ladder) => Requested.Add(1, new KeyValuePair<string, object?>("ladder", ladder));

    internal static void CountGranted(string ladder) => Granted.Add(1, new KeyValuePair<string, object?>("ladder", ladder));

    internal static void CountRejected(string ladder) => Rejected.Add(1, new KeyValuePair<string, object?>("ladder", ladder));

    internal static void CountEscalated(string ladder) => Escalated.Add(1, new KeyValuePair<string, object?>("ladder", ladder));

    internal static void CountExpired(string ladder) => Expired.Add(1, new KeyValuePair<string, object?>("ladder", ladder));

    internal static void CountWithdrawn(string ladder) => Withdrawn.Add(1, new KeyValuePair<string, object?>("ladder", ladder));
}
