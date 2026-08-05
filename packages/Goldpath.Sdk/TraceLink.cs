using System.Diagnostics;

namespace Goldpath;

/// <summary>
/// Turns a stored W3C traceparent into span links: a span born on a Quartz thread has no
/// ambient <see cref="Activity.Current"/>, so the only way it can point at the trace that
/// CAUSED the work (the upload request, the operator's trigger) is an explicit link
/// supplied at start. Shipped in Goldpath.Sdk — one seam for every module (platform RFC D1).
/// </summary>
public static class TraceLink
{
    /// <summary>Links to parse-able contexts only — a corrupt stored value never breaks a span.</summary>
    public static IEnumerable<ActivityLink>? To(string? traceParent)
        => !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, null, isRemote: true, out var context)
            ? new[] { new ActivityLink(context) }
            : null;
}
