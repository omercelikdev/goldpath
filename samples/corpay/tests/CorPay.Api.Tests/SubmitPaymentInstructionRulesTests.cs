using CorPay.Api.Payments.Features;
using Xunit;

namespace CorPay.Api.Tests;

/// <summary>The submit contract's validation table — one case per rule, spec-derived.</summary>
public class SubmitPaymentInstructionRulesTests
{
    private static SubmitPaymentInstructionCommand Valid() =>
        new("PAY-001", "TR330006100519786457841326", "DE89370400440532013000", 1250.00m, "TRY");

    [Fact]
    public void A_valid_instruction_passes_every_rule()
        => Assert.Empty(SubmitPaymentInstructionHandler.Validate(Valid()));

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Amount_must_be_positive(decimal amount)
        => Assert.Contains(SubmitPaymentInstructionHandler.Validate(Valid() with { Amount = amount }),
            e => e.PropertyName == "Amount");

    [Theory]
    [InlineData("GBP")]
    [InlineData("try")]
    [InlineData("")]
    public void Currency_is_whitelisted(string currency)
        => Assert.Contains(SubmitPaymentInstructionHandler.Validate(Valid() with { Currency = currency }),
            e => e.PropertyName == "Currency");

    [Theory]
    [InlineData("not-an-iban")]
    [InlineData("TR12")]
    [InlineData("tr330006100519786457841326")]
    public void Iban_shape_is_checked_on_both_sides(string iban)
    {
        Assert.Contains(SubmitPaymentInstructionHandler.Validate(Valid() with { DebtorIban = iban }),
            e => e.PropertyName == "DebtorIban");
        Assert.Contains(SubmitPaymentInstructionHandler.Validate(Valid() with { CreditorIban = iban }),
            e => e.PropertyName == "CreditorIban");
    }

    [Fact]
    public void Reference_is_required()
        => Assert.Contains(SubmitPaymentInstructionHandler.Validate(Valid() with { Reference = " " }),
            e => e.PropertyName == "Reference");
}
