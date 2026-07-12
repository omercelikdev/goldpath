using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Goldpath;

/// <summary>
/// API surface conventions of the golden path: URL-segment versioning, JSON wire defaults,
/// and (on net10) deterministic OpenAPI generation. Composes Asp.Versioning and
/// Microsoft.AspNetCore.OpenApi — configured, not wrapped (ADR-0003).
/// </summary>
public static class GoldpathApiDefaultsExtensions
{
    /// <summary>
    /// Adds versioning (URL segment, default v1, versions reported), JSON wire defaults
    /// (camelCase, enums as strings, null-writes ignored), and on net10 the OpenAPI document.
    /// </summary>
    public static TBuilder AddGoldpathApiDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services
            .AddApiVersioning(versioning =>
            {
                versioning.DefaultApiVersion = new ApiVersion(1);
                versioning.AssumeDefaultVersionWhenUnspecified = true;
                versioning.ReportApiVersions = true;
                versioning.ApiVersionReader = new UrlSegmentApiVersionReader();
            });

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

#if NET10_0_OR_GREATER
        builder.Services.AddOpenApi("v1");
#endif
        return builder;
    }

    /// <summary>
    /// Creates the versioned API root (<c>/api/v{version}</c>, v1 registered) that all
    /// endpoints — including Mediant <c>[HttpEndpoint]</c> mappings — attach to.
    /// In Development the interactive OpenAPI endpoint is mapped as well; production never
    /// serves the document (the build-time export artifact is the Spec Engine drift input).
    /// </summary>
    public static RouteGroupBuilder MapGoldpathApi(this WebApplication app)
    {
#if NET10_0_OR_GREATER
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
#endif
        ApiVersionSet versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        return app.MapGroup("/api/v{version:apiVersion}")
            .WithApiVersionSet(versionSet);
    }
}
