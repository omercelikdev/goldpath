using System.Security.Cryptography;
using Mediant.Behaviors.Idempotency;
using Microsoft.AspNetCore.Http;

namespace Goldpath;

/// <summary>
/// The HTTP half of the Goldpath idempotency story: honors the <c>Idempotency-Key</c> header on
/// mutating requests. First request executes and its response is stored; a retry replays it
/// byte-for-byte; a concurrent duplicate gets 409 (or waits, per options); the same key with a
/// different payload gets 422. Composed on Mediant's coordinator — the command path
/// (<c>[Idempotent]</c>) shares the store and the semantics.
/// </summary>
public sealed class GoldpathIdempotencyMiddleware
{
    /// <summary>Response header marking a replayed response.</summary>
    public const string ReplayHeader = "Goldpath-Idempotent-Replay";

    private static readonly string[] s_mutatingMethods = ["POST", "PUT", "PATCH"];

    private readonly RequestDelegate _next;
    private readonly IIdempotentOperationCoordinator _coordinator;
    private readonly GoldpathIdempotencyOptions _options;

    /// <summary>Creates the middleware.</summary>
    public GoldpathIdempotencyMiddleware(
        RequestDelegate next,
        IIdempotentOperationCoordinator coordinator,
        GoldpathIdempotencyOptions options)
    {
        _next = next;
        _coordinator = coordinator;
        _options = options;
    }

    /// <summary>Processes the request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        string? idempotencyKey = context.Request.Headers[GoldpathHeaders.IdempotencyKey];
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || !s_mutatingMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var tenant = context.RequestServices.GetService(typeof(ITenantContext)) is ITenantContext tenantContext
            ? tenantContext.Current?.Value ?? "-"
            : "-";
        var scopedKey = $"http:{tenant}:{context.Request.Method}:{context.Request.Path}:{idempotencyKey}";

        string? fingerprint = null;
        if (_options.Fingerprint == IdempotencyFingerprintMode.Strict)
        {
            fingerprint = await ComputeFingerprintAsync(context);
        }

        var lockWait = _options.OnConflict == IdempotencyConflictBehavior.Reject ? TimeSpan.Zero : (TimeSpan?)null;
        using var operation = await _coordinator.BeginAsync<StoredHttpResponse>(
            scopedKey, fingerprint, lockWait, context.RequestAborted);

        switch (operation.Status)
        {
            case IdempotentOperationStatus.Replay:
                await WriteReplayAsync(context, operation.StoredResponse!);
                return;

            case IdempotentOperationStatus.InFlight:
                await WriteProblemAsync(context, StatusCodes.Status409Conflict,
                    "A request with this Idempotency-Key is currently in flight.");
                return;

            case IdempotentOperationStatus.FingerprintMismatch:
                await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity,
                    "This Idempotency-Key was already used with a different payload.");
                return;
        }

        // New: execute, capture the response, store it on success.
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);

            if (context.Response.StatusCode is >= 200 and < 300)
            {
                var stored = new StoredHttpResponse
                {
                    StatusCode = context.Response.StatusCode,
                    ContentType = context.Response.ContentType,
                    BodyBase64 = Convert.ToBase64String(buffer.ToArray()),
                };
                await operation.CompleteAsync(stored, TimeSpan.FromHours(_options.TtlHours), CancellationToken.None);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static async Task<string> ComputeFingerprintAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(context.Request.Body, context.RequestAborted);
        context.Request.Body.Position = 0;
        return Convert.ToHexString(hash);
    }

    private static async Task WriteReplayAsync(HttpContext context, StoredHttpResponse stored)
    {
        context.Response.StatusCode = stored.StatusCode;
        context.Response.ContentType = stored.ContentType;
        context.Response.Headers[ReplayHeader] = "true";
        var body = Convert.FromBase64String(stored.BodyBase64);
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title)
    {
        context.Response.StatusCode = statusCode;
        if (context.RequestServices.GetService(typeof(IProblemDetailsService)) is IProblemDetailsService problems)
        {
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = { Status = statusCode, Title = title },
            });
        }
    }
}

/// <summary>The stored shape of an idempotent HTTP response (replayed byte-for-byte).</summary>
public sealed record StoredHttpResponse
{
    /// <summary>The original status code.</summary>
    public int StatusCode { get; init; }

    /// <summary>The original content type.</summary>
    public string? ContentType { get; init; }

    /// <summary>The original body, base64-encoded.</summary>
    public string BodyBase64 { get; init; } = "";
}
