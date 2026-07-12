using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Goldpath.Bulk.Tests;

public class OptionsTests
{
    private static readonly IServiceProvider EmptyServices = new ServiceCollection().BuildServiceProvider();

    private static GoldpathBulkRawRow Raw(params (string Field, string Value)[] fields)
        => new(1, fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void MaxRows_is_mandatory_the_ceiling_is_a_decision()
    {
        var options = new GoldpathBulkOptions();
        var e = Assert.Throws<InvalidOperationException>(() =>
            options.AddBatch<PaymentRow>("payments", b => b.RowKey(r => r.EndToEndId)));
        Assert.Contains("MaxRows", e.Message, StringComparison.Ordinal);
        Assert.Contains("GP1501", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_definition_names_refuse()
    {
        var options = new GoldpathBulkOptions();
        options.AddBatch<PaymentRow>("payments", b => b.MaxRows(10));
        Assert.Contains("already registered",
            Assert.Throws<InvalidOperationException>(() => options.AddBatch<PaymentRow>("payments", b => b.MaxRows(10))).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_definition_lookup_teaches_the_registered_names()
    {
        var options = new GoldpathBulkOptions();
        options.AddBatch<PaymentRow>("payments", b => b.MaxRows(10));
        var e = Assert.Throws<InvalidOperationException>(() => options.Definition("imports"));
        Assert.Contains("payments", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_conversion_fills_the_row_and_serializes_it()
    {
        var options = new GoldpathBulkOptions();
        options.AddBatch<PaymentRow>("payments", b => b.MaxRows(10));
        var result = options.Definition("payments").Validate(
            Raw(("EndToEndId", "E1"), ("Iban", "TR1"), ("Amount", "10.50"), ("Note", "")), EmptyServices);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Payload);
        Assert.Contains("\"Amount\":10.50", result.Payload, StringComparison.Ordinal);
        Assert.Contains("\"Note\":null", result.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversion_failure_names_the_field_but_never_echoes_the_value()
    {
        var options = new GoldpathBulkOptions();
        options.AddBatch<PaymentRow>("payments", b => b.MaxRows(10));
        var result = options.Definition("payments").Validate(
            Raw(("EndToEndId", "E1"), ("Amount", "NOT-A-NUMBER")), EmptyServices);

        var (field, message) = Assert.Single(result.Errors);
        Assert.Equal("Amount", field);
        Assert.Equal("value is not a valid Decimal", message);
        Assert.DoesNotContain("NOT-A-NUMBER", message, StringComparison.Ordinal);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void An_empty_field_on_a_non_nullable_value_type_is_a_finding()
    {
        var options = new GoldpathBulkOptions();
        options.AddBatch<PaymentRow>("payments", b => b.MaxRows(10));
        var result = options.Definition("payments").Validate(
            Raw(("EndToEndId", "E1"), ("Amount", "")), EmptyServices);
        Assert.Contains(result.Errors, e => e.Field == "Amount");
    }

    [Fact]
    public void Domain_validation_runs_only_when_conversion_succeeded_and_rowkey_only_when_valid()
    {
        var options = new GoldpathBulkOptions();
        options.AddBatch<PaymentRow>("payments", b => b
            .MaxRows(10)
            .RowKey(r => r.EndToEndId)
            .Validate((row, ctx) => ctx.Fail(nameof(row.Amount), "amount must be positive")));

        var invalid = options.Definition("payments").Validate(Raw(("Amount", "x")), EmptyServices);
        Assert.Single(invalid.Errors);              // conversion error only — the validator never saw a half-built row
        Assert.Null(invalid.RowKey);

        var valid = options.Definition("payments").Validate(Raw(("EndToEndId", "E1"), ("Amount", "5")), EmptyServices);
        Assert.Contains(valid.Errors, e => e.Message == "amount must be positive");
    }

    [Fact]
    public void Culture_drives_decimal_parsing()
    {
        var options = new GoldpathBulkOptions();
        options.AddBatch<PaymentRow>("payments", b => b.Csv(c => { c.Delimiter = ';'; c.Culture = "tr-TR"; }).MaxRows(10));
        var result = options.Definition("payments").Validate(
            Raw(("EndToEndId", "E1"), ("Amount", "10,50")), EmptyServices);
        Assert.Empty(result.Errors);
        Assert.Contains("\"Amount\":10.50", result.Payload!, StringComparison.Ordinal);
    }
}
