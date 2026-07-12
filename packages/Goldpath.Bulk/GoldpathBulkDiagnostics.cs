using System.Diagnostics;

namespace Goldpath;

/// <summary>
/// The bulk module's activity source — the tracing twin of <see cref="GoldpathBulkMetrics"/>.
/// Span names: <c>goldpath.bulk.validate</c> / <c>.execute-range</c> / <c>.replay-row</c>;
/// each links back to the batch's stored upload traceparent (per-instruction correlation).
/// </summary>
internal static class GoldpathBulkDiagnostics
{
    internal static readonly ActivitySource Source = new("Goldpath.Bulk");
}
