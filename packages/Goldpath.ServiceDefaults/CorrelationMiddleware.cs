using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>
/// Accepts or generates the <see cref="GoldpathHeaders.CorrelationId"/> header, echoes it on the
/// response, and enriches the log scope and current <see cref="Activity"/>. W3C traceparent
/// remains the tracing truth; this header exists for systems that cannot consume it.
/// </summary>
public sealed class CorrelationMiddleware
{
    /// <summary>Key under which the correlation id is stored in <see cref="HttpContext.Items"/>.</summary>
    public const string ItemKey = "Goldpath.CorrelationId";

    private readonly RequestDelegate _next;
    private readonly GoldpathServiceDefaultsOptions _options;
    private readonly ILogger<CorrelationMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    public CorrelationMiddleware(
        RequestDelegate next,
        GoldpathServiceDefaultsOptions options,
        ILogger<CorrelationMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    // The inbound id goes into a response header and every log scope — clamp it (audit
    // A7: log-injection surface): sane length, token-safe characters, nothing else.
    private static bool IsSafeCorrelationId(string? candidate)
        => candidate is { Length: > 0 and <= 128 }
            && candidate.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');

    /// <summary>Processes the request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        string? inbound = context.Request.Headers[GoldpathHeaders.CorrelationId];
        var correlationId = _options.Correlation.AcceptInbound && IsSafeCorrelationId(inbound)
            ? inbound!
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(static state =>
        {
            var (ctx, id) = ((HttpContext, string))state;
            ctx.Response.Headers[GoldpathHeaders.CorrelationId] = id;
            return Task.CompletedTask;
        }, (context, correlationId));

        Activity.Current?.SetTag("goldpath.correlation_id", correlationId);

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
