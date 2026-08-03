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
    /// <summary>Probe lifecycle states — the enum must travel as a STRING schema.</summary>
    public enum ProbeState
    {
        Active,
        Archived,
    }

    /// <summary>The probe list query — cursor/size/flag/state become documented query parameters.</summary>
    [HttpEndpoint("GET", "/api/v1/probes")]
    public record ListProbesQuery : IQuery<Result<string>>
    {
        public string? Cursor { get; init; }
        public int Size { get; init; } = 50;
        public bool IncludeArchived { get; init; }
        public ProbeState? State { get; init; }
        public required string Owner { get; init; }

        /// <summary>Computed — never a wire input, must be SKIPPED.</summary>
        public string Summary => $"{Cursor}/{Size}";
    }

    /// <summary>Mixed route+query: the route-bound id keeps its slot, verbose joins as query.</summary>
    [HttpEndpoint("GET", "/api/v1/probes/{id}")]
    public record GetProbeQuery : IQuery<Result<string>>
    {
        public long Id { get; init; }
        public bool Verbose { get; init; }
    }

    public sealed class GetProbeHandler : IQueryHandler<GetProbeQuery, Result<string>>
    {
        public ValueTask<Result<string>> Handle(GetProbeQuery request, CancellationToken cancellationToken)
            => ValueTask.FromResult(Result.Success("one"));
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

        // Enums travel as STRING schemas with their names enumerated.
        var state = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "state");
        Assert.Contains("string", state.GetProperty("schema").GetProperty("type").ToString());
        Assert.Contains("Archived", state.GetProperty("schema").GetProperty("enum").ToString());

        // `required` members document as required; computed members never appear.
        var owner = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "owner");
        Assert.True(owner.GetProperty("required").GetBoolean());
        Assert.DoesNotContain("summary", names);
    }

    [Fact]
    public async Task Route_bound_parameters_keep_their_slot()
    {
        var doc = await ExportedDocumentAsync();
        var get = doc.GetProperty("paths").GetProperty("/api/v1/probes/{id}").GetProperty("get");
        var parameters = get.GetProperty("parameters").EnumerateArray().ToList();

        var id = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "id");
        Assert.Equal("path", id.GetProperty("in").GetString());   // exactly one id, and it is the ROUTE one
        Assert.Contains(parameters, p => p.GetProperty("name").GetString() == "verbose"
            && p.GetProperty("in").GetString() == "query");
    }

    [Fact]
    public async Task Body_bound_commands_carry_a_request_body()
    {
        var doc = await ExportedDocumentAsync();
        var post = doc.GetProperty("paths").GetProperty("/api/v1/probes").GetProperty("post");

        Assert.True(post.TryGetProperty("requestBody", out var body), "the POST must document its requestBody");
        Assert.True(body.GetProperty("content").TryGetProperty("application/json", out _));
    }

    /// <summary>A stand-in context — never resolved: document export does not run handlers.</summary>
    public sealed class ProbeDb(Microsoft.EntityFrameworkCore.DbContextOptions<ProbeDb> options)
        : Microsoft.EntityFrameworkCore.DbContext(options);

    /// <summary>
    /// The RESPONSE side (#98): the admin tenant wrappers type their delegates as IResult,
    /// which exports a bodyless 200 unless the endpoint declares what it returns. This pins
    /// the four list endpoints that shipped a full train without a schema.
    /// </summary>
    [Fact]
    public async Task Tenant_wrapped_admin_lists_document_their_response_schema()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddGoldpathApiDefaults();

        var app = builder.Build();
        app.MapGoldpathApi();
        app.MapGoldpathBulkAdmin<ProbeDb>(exposeUnsecured: true);
        app.MapGoldpathArchivalAdmin<ProbeDb>(exposeUnsecured: true);
        await app.StartAsync();

        var doc = JsonDocument.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json")).RootElement;
        var paths = doc.GetProperty("paths");
        foreach (var (path, schema) in new (string, string)[]
        {
            ("/goldpath/admin/bulk/batches", "GoldpathBulkBatchInfo"),
            ("/goldpath/admin/bulk/batches/{batchId}/errors", "GoldpathBulkRowError"),
            ("/goldpath/admin/archival/holds", "GoldpathLegalHold"),
            ("/goldpath/admin/archival/erasures", "GoldpathErasureRecord"),
        })
        {
            var ok = paths.GetProperty(path).GetProperty("get").GetProperty("responses").GetProperty("200");
            Assert.True(ok.TryGetProperty("content", out var content), $"{path} must document its 200 body");
            Assert.Contains(schema, content.GetProperty("application/json").GetProperty("schema").ToString());
        }
    }
}
