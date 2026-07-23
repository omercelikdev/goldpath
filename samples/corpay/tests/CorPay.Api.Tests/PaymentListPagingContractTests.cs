using CorPay.Api.Orders;
using CorPay.Api.Payments;
using CorPay.Api.Payments.Features;
using Goldpath;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CorPay.Api.Tests;

/// <summary>
/// Spec-derived paging contract for GET /api/v1/payment-instructions
/// (specs/CorPay.Api.json, PageOfPaymentInstructionDto): `nextCursor: null` means the
/// end, `size` is the APPLIED page size, and there is deliberately no total count.
/// Written implementation-blind (goldpath-test-gen): inputs are the spec, the manifest
/// and the public handler seam — never the feature sources.
/// </summary>
public class PaymentListPagingContractTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly OrdersDbContext _db;

    public PaymentListPagingContractTests()
    {
        _connection.Open();
        _db = new SqliteOrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new GoldpathSaveChangesInterceptor(
                [new TenantStampContributor()], new GoldpathSaveContext(TimeProvider.System, null)))
            .Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void Seed(string tenant, string reference, PaymentStatus status = PaymentStatus.Submitted)
    {
        using var scope = GoldpathTenant.Use(tenant);
        _db.Set<PaymentInstruction>().Add(new PaymentInstruction
        {
            Reference = reference,
            DebtorIban = "TR330006100519786457841326",
            CreditorIban = "DE89370400440532013000",
            Amount = 10m,
            Currency = "TRY",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    /// <summary>Walks pages until the spec's end marker; returns every item seen.</summary>
    private async Task<List<PaymentInstructionDto>> WalkAsync(int take, List<int>? pageSizes = null)
    {
        var handler = new GetPaymentInstructionsHandler(_db);
        var seen = new List<PaymentInstructionDto>();
        string? cursor = null;
        var guard = 0;
        do
        {
            var result = await handler.Handle(new GetPaymentInstructionsQuery { Cursor = cursor, Size = take }, CancellationToken.None);
            Assert.True(result.IsSuccess);
            seen.AddRange(result.Value.Items);
            pageSizes?.Add(result.Value.Size);
            cursor = result.Value.NextCursor;
            Assert.True(++guard <= 50, "the walk must terminate — nextCursor never became null");
        }
        while (cursor is not null);
        return seen;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public async Task The_walk_yields_every_row_exactly_once_and_ends_with_null_cursor(int take)
    {
        var refs = Enumerable.Range(1, 7).Select(i => $"PAGE-{i:D2}").ToArray();
        foreach (var r in refs)
        {
            Seed("acme", r);
        }

        using var tenant = GoldpathTenant.Use("acme");
        var seen = await WalkAsync(take);

        Assert.Equal(refs.Order(), seen.Select(i => i.Reference).Order());
    }

    [Fact]
    public async Task Size_is_the_applied_page_size_on_every_page()
    {
        foreach (var i in Enumerable.Range(1, 7))
        {
            Seed("acme", $"SIZE-{i:D2}");
        }

        using var tenant = GoldpathTenant.Use("acme");
        var sizes = new List<int>();
        var seen = await WalkAsync(take: 3, sizes);

        Assert.Equal(7, seen.Count);
        foreach (var (size, index) in sizes.Select((s, i) => (s, i)))
        {
            // The spec: `size` is the APPLIED page size — it describes the request's
            // effective take, so every page of the same walk reports the same value.
            Assert.Equal(3, size);
            _ = index;
        }
    }

    [Fact]
    public async Task The_list_is_tenant_fenced()
    {
        Seed("acme", "MINE-1");
        Seed("acme", "MINE-2");
        Seed("acme", "MINE-3");
        Seed("rival", "THEIRS-1");
        Seed("rival", "THEIRS-2");

        using var tenant = GoldpathTenant.Use("acme");
        var seen = await WalkAsync(take: 10);

        Assert.Equal(3, seen.Count);
        Assert.All(seen, i => Assert.StartsWith("MINE-", i.Reference));
    }

    [Fact]
    public async Task Status_round_trips_every_state_the_spec_declares()
    {
        PaymentStatus[] specStates =
            [PaymentStatus.Submitted, PaymentStatus.PendingApproval, PaymentStatus.Executed, PaymentStatus.Rejected, PaymentStatus.Failed];
        foreach (var (state, index) in specStates.Select((s, i) => (s, i)))
        {
            Seed("acme", $"ST-{index}", state);
        }

        using var tenant = GoldpathTenant.Use("acme");
        var seen = await WalkAsync(take: 10);

        Assert.Equal(specStates.Order(), seen.Select(i => i.Status).Order());
    }
}
