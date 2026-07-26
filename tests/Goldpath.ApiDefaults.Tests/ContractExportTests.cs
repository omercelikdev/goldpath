using System.Text.Json;
using Mediant.Abstractions;
using Mediant.AspNetCore.Attributes;
using Mediant.AspNetCore.Mapping;
using Mediant.Results;
using Mediant.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Goldpath.Tests;

/// <summary>
/// The exported contract carries the REQUEST side (contract-export completeness):
/// query-bound Mediant request properties surface as documented parameters, and
/// body-bound commands carry a requestBody — the drift input must describe what
/// callers SEND, not only what they receive.
/// </summary>
public class ContractExportTests
{
    /// <summary>The probe list query — cursor/size/flag should become documented query parameters.</summary>
    [HttpEndpoint("GET", "/api/v1/probes")]
    public record ListProbesQuery : IQuery<Result<string>>
    {
        public string? Cursor { get; init; }
        public int Size { get; init; } = 50;
        public bool IncludeArchived { get; init; }
    }

    /// <summary>The probe submit command — its schema should become the requestBody.</summary>
    [HttpEndpoint("POST", "/api/v1/probes")]
    public record SubmitProbeCommand(string Name) : ICommand<Result<long>>;

    public sealed class ListProbesHandler : IQueryHandler<ListProbesQuery, Result<string>>
    {
        public ValueTask<Result<string>> Handle(ListProbesQuery request, CancellationToken cancellationToken)
            => ValueTask.FromResult(Result.Success("ok"));
    }

    public sealed class SubmitProbeHandler : ICommandHandler<SubmitProbeCommand, Result<long>>
    {
        public ValueTask<Result<long>> Handle(SubmitProbeCommand request, CancellationToken cancellationToken)
            => ValueTask.FromResult(Result.Success(42L));
    }

    private static async Task<JsonElement> ExportedDocumentAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddGoldpathApiDefaults();
        builder.Services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(ContractExportTests).Assembly));

        var app = builder.Build();
        app.MapGoldpathApi();
        app.MapMediantEndpoints(typeof(ContractExportTests).Assembly);
        await app.StartAsync();

        var json = await app.GetTestClient().GetStringAsync("/openapi/v1.json");
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task Query_bound_request_properties_become_documented_parameters()
    {
        var doc = await ExportedDocumentAsync();
        var get = doc.GetProperty("paths").GetProperty("/api/v1/probes").GetProperty("get");

        Assert.True(get.TryGetProperty("parameters", out var parameters), "the GET must document its parameters");
        var names = parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Contains("cursor", names);
        Assert.Contains("size", names);
        Assert.Contains("includeArchived", names);

        var size = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "size");
        Assert.Equal("query", size.GetProperty("in").GetString());
        Assert.Contains("integer", size.GetProperty("schema").GetProperty("type").ToString());
    }

    [Fact]
    public async Task Body_bound_commands_carry_a_request_body()
    {
        var doc = await ExportedDocumentAsync();
        var post = doc.GetProperty("paths").GetProperty("/api/v1/probes").GetProperty("post");

        Assert.True(post.TryGetProperty("requestBody", out var body), "the POST must document its requestBody");
        Assert.True(body.GetProperty("content").TryGetProperty("application/json", out _));
    }
}
