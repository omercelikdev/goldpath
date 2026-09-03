using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Goldpath;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Goldpath.FileExchange.Tests;

/// <summary>
/// The admin surface the console federates on (§7.1: the API is the contract): the rails
/// root with live counts, the files view, the quarantine with reasons and age — driven over
/// BOTH shipped ledgers, and over real HTTP for the frozen routes and the R3 query shape.
/// Read-only by contract: re-delivering the file IS the reprocess.
/// </summary>
public sealed class FileExchangeAdminTests : IDisposable
{
    private sealed record Row(string Reference, decimal Amount);

    public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddGoldpathFileExchangeModel();
    }

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-03T06:00:00Z");
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public FileExchangeAdminTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _provider = new ServiceCollection()
            .AddDbContext<LedgerDbContext>(b => b.UseSqlite(_connection))
            .BuildServiceProvider(true);
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private static GoldpathFileExchangeOptions Options() => new GoldpathFileExchangeOptions()
        .AddRail<Row>("registry-daily", rail => rail
            .Header(1)
            .ParseLine(line =>
            {
                var parts = line.Split(';');
                return new Row(parts[0], decimal.Parse(parts[1]));
            })
            .ValidateRow(row => row.Amount > 0 ? null : "non-positive amount")
            .Handle((_, _) => Task.CompletedTask))
        .AddRail<Row>("bank-status", rail => rail
            .ParseLine(line => new Row(line, 1m))
            .Handle((_, _) => Task.CompletedTask));

    private static GoldpathFileRailEngine Engine(GoldpathFileExchangeOptions options, IGoldpathFileLedger ledger)
        => new(options, ledger, NullLogger<GoldpathFileRailEngine>.Instance);

    private GoldpathEfFileLedger<LedgerDbContext> EfLedger(TimeProvider clock)
        => new(_provider.GetRequiredService<IServiceScopeFactory>(), clock);

    public static TheoryData<string> Ledgers => new() { "memory", "ef" };

    private IGoldpathFileLedger Ledger(string kind, TimeProvider clock)
        => kind == "memory" ? new GoldpathInMemoryFileLedger(clock) : EfLedger(clock);

    [Theory]
    [MemberData(nameof(Ledgers))]
    public async Task Rails_carry_live_counts_and_files_come_newest_archive_first(string kind)
    {
        var clock = new FakeTimeProvider(T0);
        var ledger = Ledger(kind, clock);
        var options = Options();
        var engine = Engine(options, ledger);
        var admin = new GoldpathFileExchangeAdminService(options, (IGoldpathFileLedgerQueries)ledger);

        await engine.ProcessAsync("registry-daily", "reg-0901.csv", ["header", "A;10", "B;0", "C;5"]);
        clock.Advance(TimeSpan.FromMinutes(10));
        await engine.ProcessAsync("registry-daily", "reg-0902.csv", ["header", "D;1"]);

        var rails = await admin.GetRailsAsync(CancellationToken.None);
        Assert.Equal(["bank-status", "registry-daily"], rails.Select(r => r.Name));   // declared order is alphabetical, an empty rail still answers
        var registry = rails.Single(r => r.Name == "registry-daily");
        Assert.Equal(2, registry.FilesArchived);
        Assert.Equal(1, registry.QuarantineDepth);
        Assert.Equal(T0.AddMinutes(10), registry.LastArchivedAt);
        Assert.Equal(0, rails.Single(r => r.Name == "bank-status").FilesArchived);

        var files = await admin.GetFilesAsync(null, 50, CancellationToken.None);
        Assert.Equal(["reg-0902.csv", "reg-0901.csv"], files.Select(f => f.File));   // newest archive first
        var first = files.Single(f => f.File == "reg-0901.csv");
        Assert.Equal(2, first.ProcessedRows);
        Assert.Equal(1, first.QuarantinedRows);
        Assert.True(first.Archived);
        Assert.Equal(T0, first.ArchivedAt);
    }

    [Theory]
    [MemberData(nameof(Ledgers))]
    public async Task Quarantine_comes_oldest_first_with_its_reason_and_keeps_the_first_time_on_reprocess(string kind)
    {
        var clock = new FakeTimeProvider(T0);
        var ledger = Ledger(kind, clock);
        var options = Options();
        var engine = Engine(options, ledger);
        var admin = new GoldpathFileExchangeAdminService(options, (IGoldpathFileLedgerQueries)ledger);

        await engine.ProcessAsync("registry-daily", "reg-0901.csv", ["header", "A;0"]);
        clock.Advance(TimeSpan.FromHours(1));
        await engine.ProcessAsync("bank-status", "bank-1.txt", ["ok"]);
        await engine.ProcessAsync("registry-daily", "reg-0902.csv", ["header", "B;-1"]);
        clock.Advance(TimeSpan.FromHours(1));
        // Reprocess: the same row fails again — its AGE must not reset.
        await engine.ProcessAsync("registry-daily", "reg-0901.csv", ["header", "A;0"]);

        var all = await admin.GetQuarantineAsync(null, null, 200, CancellationToken.None);
        Assert.Equal(["reg-0901.csv", "reg-0902.csv"], all.Select(q => q.File));
        Assert.Equal("non-positive amount", all[0].Reason);
        Assert.Equal(T0, all[0].QuarantinedAt);
        Assert.Equal(2, all[0].Line);   // 1-based: header is line 1

        // R3: ?file= narrows; ?rail= with one value goes to the ledger, with two values ORs on the client.
        var one = await admin.GetQuarantineAsync(null, ["reg-0902.csv"], 200, CancellationToken.None);
        Assert.Equal(T0.AddHours(1), Assert.Single(one).QuarantinedAt);
        var byRail = await admin.GetQuarantineAsync(["bank-status", "registry-daily"], null, 200, CancellationToken.None);
        Assert.Equal(2, byRail.Count);
        Assert.Empty(await admin.GetQuarantineAsync(["bank-status"], null, 200, CancellationToken.None));
    }

    [Fact]
    public async Task Take_is_clamped_and_files_filter_by_rail()
    {
        var ledger = new GoldpathInMemoryFileLedger(new FakeTimeProvider(T0));
        var options = Options();
        var engine = Engine(options, ledger);
        var admin = new GoldpathFileExchangeAdminService(options, ledger);
        for (var i = 0; i < 3; i++)
        {
            await engine.ProcessAsync("bank-status", $"bank-{i}.txt", ["ok"]);
        }

        await engine.ProcessAsync("registry-daily", "reg.csv", ["header", "A;1"]);

        Assert.Single(await admin.GetFilesAsync(null, 0, CancellationToken.None));   // zero asks still answer one row, honestly
        Assert.Equal(3, (await admin.GetFilesAsync(["bank-status"], 50, CancellationToken.None)).Count);
        Assert.Equal(4, (await admin.GetFilesAsync(["bank-status", "registry-daily"], 50, CancellationToken.None)).Count);
        Assert.Equal(2, (await admin.GetFilesAsync(["bank-status", "registry-daily"], 2, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task The_routes_answer_over_real_HTTP_with_the_R3_query_shape()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.AddGoldpathFileExchange(files => files
            .AddRail<Row>("registry-daily", rail => rail
                .Header(1)
                .ParseLine(line => new Row(line.Split(';')[0], decimal.Parse(line.Split(';')[1])))
                .ValidateRow(row => row.Amount > 0 ? null : "non-positive amount")
                .Handle((_, _) => Task.CompletedTask)));
        await using var app = builder.Build();
        app.MapGoldpathFileExchangeAdmin(exposeUnsecured: true);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // The probe root answers with the declared rail BEFORE any file arrived (an empty rail is still a rail).
        var empty = await client.GetFromJsonAsync<JsonElement>("/goldpath/admin/fileexchange/rails");
        Assert.Equal("registry-daily", Assert.Single(empty.EnumerateArray()).GetProperty("name").GetString());

        using (var scope = app.Services.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<GoldpathFileRailEngine>();
            await engine.ProcessAsync("registry-daily", "reg.csv", ["header", "A;1", "B;0"]);
        }

        var rails = await client.GetFromJsonAsync<JsonElement>("/goldpath/admin/fileexchange/rails");
        Assert.Equal(1, rails[0].GetProperty("quarantineDepth").GetInt32());

        var files = await client.GetFromJsonAsync<JsonElement>("/goldpath/admin/fileexchange/files?rail=registry-daily&take=50");
        Assert.Equal("reg.csv", Assert.Single(files.EnumerateArray()).GetProperty("file").GetString());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/goldpath/admin/fileexchange/files?rail=other")).EnumerateArray());

        var quarantine = await client.GetFromJsonAsync<JsonElement>("/goldpath/admin/fileexchange/quarantine?rail=registry-daily&file=reg.csv");
        var row = Assert.Single(quarantine.EnumerateArray());
        Assert.Equal(3, row.GetProperty("line").GetInt32());
        Assert.Equal("non-positive amount", row.GetProperty("reason").GetString());

        // No verbs on this surface by contract — a POST anywhere under it is a 404/405, never a silent 200.
        var post = await client.PostAsync("/goldpath/admin/fileexchange/quarantine", null);
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
    }

    private sealed class ReadlessLedger : IGoldpathFileLedger
    {
        public Task<bool> IsProcessedAsync(string rail, string file, int line, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task MarkProcessedAsync(string rail, string file, int line, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task QuarantineAsync(string rail, string file, int line, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReleaseQuarantineAsync(string rail, string file, int line, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<GoldpathQuarantinedRow>> GetQuarantineAsync(string rail, string file, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GoldpathQuarantinedRow>>([]);
        public Task MarkArchivedAsync(string rail, string file, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_custom_ledger_without_the_reads_is_refused_at_startup_in_words()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IGoldpathFileLedger, ReadlessLedger>();
        builder.AddGoldpathFileExchange(files => files.AddRail<Row>("r", rail => rail.ParseLine(l => new Row(l, 1m)).Handle((_, _) => Task.CompletedTask)));
        await using var app = builder.Build();

        var refusal = Assert.Throws<InvalidOperationException>(() => app.MapGoldpathFileExchangeAdmin(exposeUnsecured: true));
        Assert.Contains("IGoldpathFileLedgerQueries", refusal.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// The corners the first mutation run left alive (2026-09-03, 64%): in-flight files ahead
/// of archived ones, tie-breaking, case-insensitive rail filters, the window-then-take
/// logic behind multi-value filters, and the empty rail's nulls — over BOTH ledgers.
/// </summary>
public sealed class FileExchangeAdminOrderingTests : IDisposable
{
    private sealed record Row(string Reference, decimal Amount);

    public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddGoldpathFileExchangeModel();
    }

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-03T06:00:00Z");
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public FileExchangeAdminOrderingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _provider = new ServiceCollection()
            .AddDbContext<LedgerDbContext>(b => b.UseSqlite(_connection))
            .BuildServiceProvider(true);
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    public static TheoryData<string> Ledgers => new() { "memory", "ef" };

    private IGoldpathFileLedger Ledger(string kind, TimeProvider clock)
        => kind == "memory" ? new GoldpathInMemoryFileLedger(clock) : new GoldpathEfFileLedger<LedgerDbContext>(_provider.GetRequiredService<IServiceScopeFactory>(), clock);

    private static GoldpathFileExchangeOptions Options() => new GoldpathFileExchangeOptions()
        .AddRail<Row>("Registry-Daily", rail => rail.ParseLine(l => new Row(l, l.StartsWith('!') ? 0m : 1m)).ValidateRow(r => r.Amount > 0 ? null : "bad").Handle((_, _) => Task.CompletedTask))
        .AddRail<Row>("bank-status", rail => rail.ParseLine(l => new Row(l, 1m)).Handle((_, _) => Task.CompletedTask));

    [Theory]
    [MemberData(nameof(Ledgers))]
    public async Task An_in_flight_file_comes_first_and_ties_break_by_rail_then_file(string kind)
    {
        var clock = new FakeTimeProvider(T0);
        var ledger = Ledger(kind, clock);
        var queries = (IGoldpathFileLedgerQueries)ledger;

        // Two files archived at the SAME instant, on two rails; one file only quarantined (never archived).
        await ledger.MarkArchivedAsync("bank-status", "b-2.txt");
        await ledger.MarkArchivedAsync("Registry-Daily", "a-1.csv");
        await ledger.MarkProcessedAsync("Registry-Daily", "a-1.csv", 2);
        await ledger.QuarantineAsync("Registry-Daily", "in-flight.csv", 5, "bad");
        await ledger.MarkProcessedAsync("bank-status", "only-processed.txt", 1);

        var files = await queries.ListFilesAsync(null, 50);
        // In-flight (unarchived) first — the two unarchived files order by rail then file —
        // then the archived ties by rail ("Registry-Daily" sorts before "bank-status" ordinally).
        Assert.Equal(["in-flight.csv", "only-processed.txt", "a-1.csv", "b-2.txt"], files.Select(f => f.File));
        var inFlight = files[0];
        Assert.False(inFlight.Archived);
        Assert.Null(inFlight.ArchivedAt);
        Assert.Equal(1, inFlight.QuarantinedRows);
        Assert.Equal(0, inFlight.ProcessedRows);
        Assert.Equal(1, files.Single(f => f.File == "a-1.csv").ProcessedRows);
        Assert.Equal(0, files.Single(f => f.File == "b-2.txt").ProcessedRows);

        // Take truncates AFTER ordering; the rail filter is exact on the ledger.
        Assert.Equal(["in-flight.csv", "only-processed.txt"], (await queries.ListFilesAsync(null, 2)).Select(f => f.File));
        Assert.Equal(["only-processed.txt", "b-2.txt"], (await queries.ListFilesAsync("bank-status", 50)).Select(f => f.File));
    }

    [Theory]
    [MemberData(nameof(Ledgers))]
    public async Task Quarantine_ties_break_by_rail_file_and_line_and_the_rail_filter_is_exact(string kind)
    {
        var clock = new FakeTimeProvider(T0);
        var ledger = Ledger(kind, clock);
        var queries = (IGoldpathFileLedgerQueries)ledger;

        await ledger.QuarantineAsync("bank-status", "b.txt", 9, "x");
        await ledger.QuarantineAsync("bank-status", "a.txt", 3, "x");
        await ledger.QuarantineAsync("bank-status", "a.txt", 1, "x");
        await ledger.QuarantineAsync("Registry-Daily", "z.csv", 7, "x");
        clock.Advance(TimeSpan.FromMinutes(1));
        await ledger.QuarantineAsync("Registry-Daily", "a.csv", 1, "later");

        var rows = await queries.ListQuarantineAsync(null, 50);
        Assert.Equal([("Registry-Daily", "z.csv", 7), ("bank-status", "a.txt", 1), ("bank-status", "a.txt", 3), ("bank-status", "b.txt", 9), ("Registry-Daily", "a.csv", 1)],
            rows.Select(r => (r.Rail, r.File, r.Line)));
        Assert.Equal("later", rows[^1].Reason);
        Assert.Equal(2, (await queries.ListQuarantineAsync(null, 2)).Count);
        Assert.Equal(3, (await queries.ListQuarantineAsync("bank-status", 50)).Count);
        Assert.Empty(await queries.ListQuarantineAsync("nope", 50));
    }

    [Theory]
    [MemberData(nameof(Ledgers))]
    public async Task The_admin_views_match_rails_case_insensitively_and_window_multi_value_filters(string kind)
    {
        var clock = new FakeTimeProvider(T0);
        var ledger = Ledger(kind, clock);
        var options = Options();
        var engine = new GoldpathFileRailEngine(options, ledger, NullLogger<GoldpathFileRailEngine>.Instance);
        var admin = new GoldpathFileExchangeAdminService(options, (IGoldpathFileLedgerQueries)ledger);

        await engine.ProcessAsync("Registry-Daily", "r-1.csv", ["ok", "!bad"]);
        clock.Advance(TimeSpan.FromMinutes(1));
        await engine.ProcessAsync("bank-status", "b-1.txt", ["ok"]);
        clock.Advance(TimeSpan.FromMinutes(1));
        await engine.ProcessAsync("bank-status", "b-2.txt", ["ok"]);

        // Rails: counts per rail, LastArchivedAt per rail, alphabetical by declared name (ordinal: uppercase first).
        var rails = await admin.GetRailsAsync(CancellationToken.None);
        Assert.Equal(["Registry-Daily", "bank-status"], rails.Select(r => r.Name));
        Assert.Equal(1, rails[0].FilesArchived);
        Assert.Equal(1, rails[0].QuarantineDepth);
        Assert.Equal(T0, rails[0].LastArchivedAt);
        Assert.Equal(2, rails[1].FilesArchived);
        Assert.Equal(0, rails[1].QuarantineDepth);
        Assert.Equal(T0.AddMinutes(2), rails[1].LastArchivedAt);

        // A single-value rail filter reaches the ledger as-is (exact on the store — the
        // console sends the declared name); a multi-value filter windows then ORs, case-insensitively.
        Assert.Equal(["b-2.txt", "b-1.txt"], (await admin.GetFilesAsync(["bank-status"], 50, CancellationToken.None)).Select(f => f.File));
        Assert.Equal(["b-2.txt", "b-1.txt", "r-1.csv"], (await admin.GetFilesAsync(["BANK-STATUS", "registry-daily"], 50, CancellationToken.None)).Select(f => f.File));
        Assert.Equal(["b-2.txt"], (await admin.GetFilesAsync(["BANK-STATUS", "registry-daily"], 1, CancellationToken.None)).Select(f => f.File));
        Assert.Equal(["r-1.csv"], (await admin.GetFilesAsync(["registry-daily", "nope"], 50, CancellationToken.None)).Select(f => f.File));

        // Quarantine: multi-value rail OR + file AND, case-insensitive on both; take after filtering.
        Assert.Single(await admin.GetQuarantineAsync(["REGISTRY-DAILY", "bank-status"], ["R-1.CSV"], 50, CancellationToken.None));
        Assert.Empty(await admin.GetQuarantineAsync(["Registry-Daily", "bank-status"], ["other.csv"], 50, CancellationToken.None));
        Assert.Single(await admin.GetQuarantineAsync(null, ["r-1.csv"], 1, CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_rail_reports_zero_counts_and_no_last_archive()
    {
        var ledger = new GoldpathInMemoryFileLedger(new FakeTimeProvider(T0));
        var options = Options();
        var admin = new GoldpathFileExchangeAdminService(options, ledger);

        var rails = await admin.GetRailsAsync(CancellationToken.None);
        Assert.All(rails, rail => Assert.Equal((0, 0, (DateTimeOffset?)null), (rail.FilesArchived, rail.QuarantineDepth, rail.LastArchivedAt)));
        Assert.Equal(0, rails.Single(r => r.Name == "bank-status").HeaderLines);
        Assert.Empty(await admin.GetFilesAsync(null, 50, CancellationToken.None));
        Assert.Empty(await admin.GetQuarantineAsync(null, null, 50, CancellationToken.None));
    }
}
