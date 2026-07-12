using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Goldpath;

/// <summary>
/// The Ring A floor in one call: telemetry, health, ProblemDetails, correlation,
/// HTTP resilience + service discovery, and the global concurrency guard.
/// Pillars are Microsoft packages configured with corporate opinion (ADR-0003) —
/// tunable via <see cref="GoldpathServiceDefaultsOptions"/>, never individually disableable.
/// </summary>
public static class GoldpathServiceDefaultsExtensions
{
    /// <summary>
    /// Adds all Ring A pillars. Options bind from the <c>Goldpath:ServiceDefaults</c>
    /// configuration section first; <paramref name="configure"/> applies on top.
    /// </summary>
    public static TBuilder AddGoldpathServiceDefaults<TBuilder>(this TBuilder builder, Action<GoldpathServiceDefaultsOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        var options = new GoldpathServiceDefaultsOptions();
        builder.Configuration.GetSection("Goldpath:ServiceDefaults").Bind(options);
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        AddTelemetry(builder, options);
        AddHealth(builder);
        AddProblemDetails(builder);
        AddHttpClientDefaults(builder);
        AddConcurrencyGuard(builder, options);
        builder.Services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, GoldpathPipelineStartupFilter>();

        return builder;
    }

    /// <summary>
    /// Maps <c>/health/live</c> (self) and <c>/health/ready</c> (all registered checks).
    /// Always mapped — K8s/OpenShift probes need them in every environment (RFC D2);
    /// the default plain-text payload never leaks check details.
    /// </summary>
    public static WebApplication MapGoldpathDefaultEndpoints(this WebApplication app)
    {
        // AllowAnonymous is explicit: probes must keep working when Goldpath.Auth's
        // secure-by-default fallback policy is active (no-op without auth).
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static r => r.Tags.Contains("live"),
        }).AllowAnonymous();
        app.MapHealthChecks("/health/ready").AllowAnonymous();
        return app;
    }

    private static void AddTelemetry(IHostApplicationBuilder builder, GoldpathServiceDefaultsOptions options)
    {
        var isDevelopment = builder.Environment.IsDevelopment();
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceVersion: typeof(GoldpathServiceDefaultsExtensions).Assembly.GetName().Version?.ToString()))
            .WithLogging(static _ => { }, static options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            })
            .WithMetrics(metrics => metrics
                .AddRuntimeInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Without these subscriptions NOTHING a Goldpath module measures leaves the
                // process — the meters exist, the collector never hears them. "Goldpath.*"
                // covers every module meter, present and future; MassTransit covers
                // broker consume/fault when messaging is wired (a no-op otherwise).
                .AddMeter("Goldpath.*")
                .AddMeter("MassTransit"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    // The tracing twin of the meter subscriptions above: without these
                    // sources every span a Goldpath module starts is a no-op — the
                    // run/chunk/replay chain never reaches the collector. MassTransit
                    // covers broker spans when messaging is wired (a no-op otherwise).
                    .AddSource("Goldpath.*")
                    .AddSource("MassTransit")
                    .SetSampler(CreateSampler(options, isDevelopment));

                if (isDevelopment)
                {
                    // RFC D4: local DX — spans visible in the console without any collector.
                    tracing.AddConsoleExporter();
                }
            });

        // OTLP for every signal when an endpoint is configured (Aspire/collector convention).
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
    }

    private static Sampler CreateSampler(GoldpathServiceDefaultsOptions options, bool isDevelopment)
    {
        if (isDevelopment)
        {
            return new AlwaysOnSampler(); // RFC D4
        }

        if (options.Observability.SamplingRatio is { } ratio)
        {
            return new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio));
        }

        return options.Observability.Profile switch
        {
            ObservabilityProfile.Full => new AlwaysOnSampler(),
            ObservabilityProfile.Minimal => new ParentBasedSampler(new TraceIdRatioBasedSampler(0.01)),
            _ => new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)),
        };
    }

    private static void AddHealth(IHostApplicationBuilder builder)
        => builder.Services.AddHealthChecks()
            .AddCheck("self", static () => HealthCheckResult.Healthy(), tags: ["live"]);

    private static void AddProblemDetails(IHostApplicationBuilder builder)
        => builder.Services.AddProblemDetails(problem => problem.CustomizeProblemDetails = ctx =>
        {
            if (ctx.HttpContext.Items.TryGetValue(CorrelationMiddleware.ItemKey, out var correlationId))
            {
                ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
            }

            ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
        });

    private static void AddHttpClientDefaults(IHostApplicationBuilder builder)
        => builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

    private static void AddConcurrencyGuard(IHostApplicationBuilder builder, GoldpathServiceDefaultsOptions options)
        => builder.Services.AddRateLimiter(limiter =>
        {
            var concurrencyOptions = new ConcurrencyLimiterOptions
            {
                PermitLimit = options.RateLimiting.ConcurrencyLimit,
                QueueLimit = options.RateLimiting.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            };
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetConcurrencyLimiter("global", _ => concurrencyOptions));
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = static async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var problemService = context.HttpContext.RequestServices.GetService<IProblemDetailsService>();
                if (problemService is not null)
                {
                    await problemService.WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails =
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = "Too many concurrent requests.",
                        },
                    });
                }
            };
        });
}

/// <summary>
/// Places the Ring A middleware ahead of user middleware:
/// correlation → exception handler (ProblemDetails, non-dev) → concurrency guard.
/// </summary>
internal sealed class GoldpathPipelineStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<CorrelationMiddleware>();

        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment())
        {
            app.UseExceptionHandler();
        }

        app.UseRateLimiter();
        next(app);
    };
}
