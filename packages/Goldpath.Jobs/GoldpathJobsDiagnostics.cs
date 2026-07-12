using System.Diagnostics;

namespace Goldpath;

/// <summary>
/// The jobs module's activity source — the tracing twin of <see cref="GoldpathJobsMetrics"/>:
/// ServiceDefaults subscribes "Goldpath.*", so spans started here reach the collector with
/// zero per-app wiring. Span names: <c>goldpath.job.run</c> / <c>.chunk</c> / <c>.replay</c>
/// / <c>.replay-item</c>.
/// </summary>
internal static class GoldpathJobsDiagnostics
{
    internal static readonly ActivitySource Source = new("Goldpath.Jobs");
}
