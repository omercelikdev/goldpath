using CorPay.Api.Orders;
using CorPay.Api.Payments;
using CorPay.Api.Payments.Features;
using Goldpath;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CorPay.Api.Tests;

/// <summary>
/// BREAKER — boundary/unicode abuse on the submit validation table (G1) and paging under
/// hostile query parameters + concurrent inserts (G2). The submit contract exposes the
/// static <c>Validate</c> seam; a hostile consumer probes the guards the contract keeps
/// invisible (currency whitelist exactness, IBAN shape, amount scale).
/// </summary>
public class BreakerSubmitValidationTests
{
    private static SubmitPaymentInstructionCommand Valid() =>
        new("PAY-001", "TR330006100519786457841326", "DE89370400440532013000", 1250.00m, "TRY");

    private static bool Flags(SubmitPaymentInstructionCommand cmd, string property) =>
        SubmitPaymentInstructionHandler.Validate(cmd).Any(e => e.PropertyName == property);

    [Theory]
    [InlineData("٣")]   // Arabic-Indic digit THREE ٣ swapped for ASCII '3'
    [InlineData("３")]   // Full-width digit THREE ３
    public void Breaker_unicode_digit_iban_is_rejected_on_both_sides(string digitLookalike)
    {
        // Replace the first ASCII digit of a valid IBAN with a Unicode digit look-alike.
        var poisoned = "TR" + digitLookalike + "30006100519786457841326";
        Assert.True(Flags(Valid() with { DebtorIban = poisoned }, "DebtorIban"),
            "a Unicode-digit IBAN must fail the shape check on the debtor side");
        Assert.True(Flags(Valid() with { CreditorIban = poisoned }, "CreditorIban"),
            "a Unicode-digit IBAN must fail the shape check on the creditor side");
    }

    [Theory]
    [InlineData("ТRY")]   // Cyrillic Te (Т) + RY — homoglyph of TRY
    [InlineData("TRУ")]   // TR + Cyrillic U (У) — homoglyph of TRY
    public void Breaker_currency_homoglyph_is_not_the_whitelisted_currency(string homoglyph)
        => Assert.True(Flags(Valid() with { Currency = homoglyph }, "Currency"),
            "a homoglyph is not the whitelisted TRY and must be rejected");

    [Theory]
    [InlineData("TRY ")]     // trailing space
    [InlineData(" TRY")]     // leading space
    [InlineData("TRYX")]     // four letters
    [InlineData("TR")]       // two letters
    [InlineData("Try")]      // mixed case
    public void Breaker_currency_must_be_exact_three_uppercase(string currency)
        => Assert.True(Flags(Valid() with { Currency = currency }, "Currency"),
            $"currency '{currency}' is not an exact whitelisted code");

    [Fact]
    public void Breaker_subcent_amount_scale_probe()
    {
        // The spec is silent on scale (G1). Money systems settle in minor units; a
        // sub-cent amount is a caller poisoning the ledger. This pins the ACTUAL guard:
        // if it is NOT flagged, the gap is real and this assertion is the alarm.
        Assert.True(Flags(Valid() with { Amount = 0.001m }, "Amount"),
            "sub-minor-unit amount 0.001 must be rejected by a treasury validator");
    }
}

/// <summary>
/// BREAKER — paging (GET /payment-instructions) under hostile size and concurrent inserts.
/// The contract (PageOfPaymentInstructionDto) promises `nextCursor: null` = end. A page
/// that returns zero rows AND a non-null cursor is an infinite-walk / DoS. Keyset paging
/// must not duplicate or skip rows that were already present when the walk began.
/// </summary>
public class BreakerPagingTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly OrdersDbContext _db;

    public BreakerPagingTests()
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
        GC.SuppressFinalize(this);
    }

    private void Seed(string tenant, string reference)
    {
        using var scope = GoldpathTenant.Use(tenant);
        _db.Set<PaymentInstruction>().Add(new PaymentInstruction
        {
            Reference = reference,
            DebtorIban = "TR330006100519786457841326",
            CreditorIban = "DE89370400440532013000",
            Amount = 10m,
            Currency = "TRY",
            Status = PaymentStatus.Submitted,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Breaker_nonpositive_size_never_returns_a_dangling_cursor(int size)
    {
        foreach (var i in Enumerable.Range(1, 5))
        {
            Seed("acme", $"SZ-{i:D2}");
        }

        using var tenant = GoldpathTenant.Use("acme");
        var handler = new GetPaymentInstructionsHandler(_db);
        var result = await handler.Handle(
            new GetPaymentInstructionsQuery { Cursor = null, Size = size }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The trap: an empty page that still hands back a cursor means a client walk loops
        // forever. Either the size is clamped up (items returned) or the walk ends (null).
        if (result.Value.Items.Count == 0)
        {
            Assert.Null(result.Value.NextCursor);
        }
        Assert.True(result.Value.Size >= 0, "the applied size must never be reported negative");
    }

    [Fact]
    public async Task Breaker_absurd_size_is_clamped_not_honored_verbatim()
    {
        foreach (var i in Enumerable.Range(1, 5))
        {
            Seed("acme", $"BIG-{i:D2}");
        }

        using var tenant = GoldpathTenant.Use("acme");
        var handler = new GetPaymentInstructionsHandler(_db);
        var result = await handler.Handle(
            new GetPaymentInstructionsQuery { Cursor = null, Size = 10_000 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // A 10_000-row page request against a keyset list must be clamped to a sane ceiling,
        // never echoed back verbatim (the offset-trap-reborn the contract warns against).
        Assert.True(result.Value.Size <= 1_000,
            $"applied size {result.Value.Size} should be clamped to a defensive ceiling");
    }

    [Fact]
    public async Task Breaker_concurrent_inserts_never_duplicate_or_skip_stable_rows()
    {
        var original = Enumerable.Range(1, 5).Select(i => $"ORIG-{i:D2}").ToArray();
        foreach (var r in original)
        {
            Seed("acme", r);
        }

        using var tenant = GoldpathTenant.Use("acme");
        var handler = new GetPaymentInstructionsHandler(_db);

        var seen = new List<string>();
        string? cursor = null;
        var page = 0;
        var guard = 0;
        do
        {
            var result = await handler.Handle(
                new GetPaymentInstructionsQuery { Cursor = cursor, Size = 2 }, CancellationToken.None);
            Assert.True(result.IsSuccess);
            seen.AddRange(result.Value.Items.Select(i => i.Reference));
            cursor = result.Value.NextCursor;

            // Adversary inserts fresh rows mid-walk, after the first page is drawn.
            if (page == 0)
            {
                Seed("acme", "INTRUDER-A");
                Seed("acme", "INTRUDER-B");
            }
            page++;
            Assert.True(++guard <= 50, "the walk must terminate");
        }
        while (cursor is not null);

        // Every row that existed BEFORE the walk started must appear exactly once — keyset
        // paging must be stable against concurrent inserts (no duplicate, no skip).
        foreach (var r in original)
        {
            Assert.Equal(1, seen.Count(s => s == r));
        }
    }
}
