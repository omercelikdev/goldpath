#if NET10_0_OR_GREATER
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Mediant.AspNetCore.Mapping;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Goldpath;

/// <summary>
/// Projects a query-bound Mediant request's public properties into documented OpenAPI
/// query parameters. The generic dispatcher hides the request type from the framework's
/// own inference, which left exported contracts with responses only — the drift input
/// (specs/*.json) must carry the request side too.
/// </summary>
internal static class MediantQueryParameterTransformer
{
    internal static Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<MediantEndpointRequestMetadata>()
            .FirstOrDefault();
        if (metadata is null || metadata.BodyBound)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];
        var taken = new HashSet<string>(
            operation.Parameters.Select(p => p.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

        // The dispatcher hides EVERYTHING from inference — including path parameters, so
        // route-template tokens are resolved here too ({id:long} -> "id"), documented as
        // path (always required); the rest of the request's properties become query.
        var routeTokens = new HashSet<string>(
            (context.Description.RelativePath ?? string.Empty)
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(static segment => segment.StartsWith('{') && segment.EndsWith('}'))
                .Select(static segment => segment[1..^1].Split(':')[0]),
            StringComparer.OrdinalIgnoreCase);

        foreach (var property in metadata.RequestType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite && property.SetMethod is null)
            {
                continue;   // computed members are not wire inputs
            }

            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!taken.Add(name))
            {
                continue;   // already documented (framework-inferred) parameters keep their slot
            }

            var isRoute = routeTokens.Contains(name);
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = isRoute ? ParameterLocation.Path : ParameterLocation.Query,
                Required = isRoute || property.GetCustomAttribute<RequiredMemberAttribute>() is not null,
                Schema = SchemaFor(property.PropertyType),
            });
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema SchemaFor(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short))
        {
            return new OpenApiSchema { Type = JsonSchemaType.Integer };
        }

        if (underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
        {
            return new OpenApiSchema { Type = JsonSchemaType.Number };
        }

        if (underlying == typeof(bool))
        {
            return new OpenApiSchema { Type = JsonSchemaType.Boolean };
        }

        if (underlying.IsEnum)
        {
            // Enums travel as strings on the goldpath wire (JsonStringEnumConverter).
            return new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [.. Enum.GetNames(underlying)],
            };
        }

        return new OpenApiSchema { Type = JsonSchemaType.String };
    }
}
#endif
