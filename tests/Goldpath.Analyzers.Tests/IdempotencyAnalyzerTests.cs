using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Goldpath.Analyzers.Tests;

public class IdempotencyAnalyzerTests
{
    private const string Stubs = """
        namespace Mediant.Abstractions
        {
            public interface ICommand<out T> { }
            public interface IQuery<out T> { }
        }
        namespace Mediant.Behaviors.Attributes
        {
            public sealed class IdempotentAttribute : System.Attribute
            {
                public string? KeyExpression { get; set; }
            }
        }
        namespace MassTransit
        {
            public interface IConsumer<in T> where T : class { }
        }
        namespace Goldpath
        {
            public static class ComposeExtensions
            {
                public static void AddGoldpathIdempotency(this object builder) { }
                public static void AddGoldpathMessaging(this object builder) { }
                public static void AddGoldpathOutbox(this object builder) { }
            }
        }
        """;

    private static Task Verify<TAnalyzer>(string source, params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source + "\n" + Stubs,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    // ── GP1001 — commands unprotected while the layer is composed

    [Fact]
    public Task GOLDPATH1001_flags_unmarked_command_when_idempotency_is_composed()
        => Verify<CommandIdempotencyAnalyzer>("""
            public record {|#0:PayCheque|} : Mediant.Abstractions.ICommand<string>;
            public static class Program { public static void Main() => Goldpath.ComposeExtensions.AddGoldpathIdempotency(new object()); }
            """,
            new DiagnosticResult(Descriptors.CommandNotIdempotent).WithLocation(0).WithArguments("PayCheque"));

    [Fact]
    public Task GOLDPATH1001_quiet_when_the_command_is_marked()
        => Verify<CommandIdempotencyAnalyzer>("""
            [Mediant.Behaviors.Attributes.Idempotent]
            public record PayCheque : Mediant.Abstractions.ICommand<string>;
            public static class Program { public static void Main() => Goldpath.ComposeExtensions.AddGoldpathIdempotency(new object()); }
            """);

    [Fact]
    public Task GOLDPATH1001_quiet_when_idempotency_is_not_composed()
        => Verify<CommandIdempotencyAnalyzer>("""
            public record PayCheque : Mediant.Abstractions.ICommand<string>;
            """);

    [Fact]
    public Task GOLDPATH1001_quiet_on_queries_and_abstract_shapes()
        => Verify<CommandIdempotencyAnalyzer>("""
            public record GetOrders : Mediant.Abstractions.IQuery<string>;
            public abstract record CommandBase : Mediant.Abstractions.ICommand<string>;
            public static class Program { public static void Main() => Goldpath.ComposeExtensions.AddGoldpathIdempotency(new object()); }
            """);

    // ── GP1002 — broker consumers without the EF inbox

    [Fact]
    public Task GOLDPATH1002_flags_consumer_when_the_bus_is_composed_without_the_inbox()
        => Verify<ConsumerInboxAnalyzer>("""
            public sealed class PaymentTaken { }
            public sealed class {|#0:PaymentConsumer|} : MassTransit.IConsumer<PaymentTaken>
            {
            }
            public static class Program { public static void Main() => Goldpath.ComposeExtensions.AddGoldpathMessaging(new object()); }
            """,
            new DiagnosticResult(Descriptors.ConsumerWithoutInbox).WithLocation(0).WithArguments("PaymentConsumer"));

    [Fact]
    public Task GOLDPATH1002_quiet_when_the_inbox_is_wired()
        => Verify<ConsumerInboxAnalyzer>("""
            public sealed class PaymentTaken { }
            public sealed class PaymentConsumer : MassTransit.IConsumer<PaymentTaken>
            {
            }
            public static class Program
            {
                public static void Main()
                {
                    Goldpath.ComposeExtensions.AddGoldpathMessaging(new object());
                    Goldpath.ComposeExtensions.AddGoldpathOutbox(new object());
                }
            }
            """);

    [Fact]
    public Task GOLDPATH1002_quiet_in_a_consumer_library_that_composes_no_bus()
        => Verify<ConsumerInboxAnalyzer>("""
            public sealed class PaymentTaken { }
            public sealed class PaymentConsumer : MassTransit.IConsumer<PaymentTaken>
            {
            }
            """);

    // ── GP1004 — [Idempotent] with no declared and no detectable key

    [Fact]
    public Task GOLDPATH1004_flags_idempotent_command_with_undetectable_key()
        => Verify<IdempotentKeyAnalyzer>("""
            [Mediant.Behaviors.Attributes.Idempotent]
            public record {|#0:RecalculatePortfolio|}(string Currency, decimal Amount) : Mediant.Abstractions.ICommand<string>;
            """,
            new DiagnosticResult(Descriptors.IdempotentKeyUndetectable).WithLocation(0).WithArguments("RecalculatePortfolio"));

    [Fact]
    public Task GOLDPATH1004_quiet_when_a_key_expression_is_declared()
        => Verify<IdempotentKeyAnalyzer>("""
            [Mediant.Behaviors.Attributes.Idempotent(KeyExpression = "request.ChequeNo")]
            public record PayCheque(string Payee) : Mediant.Abstractions.ICommand<string>;
            """);

    [Fact]
    public Task GOLDPATH1004_quiet_when_a_natural_key_property_exists()
        => Verify<IdempotentKeyAnalyzer>("""
            [Mediant.Behaviors.Attributes.Idempotent]
            public record PayCheque(string ChequeNo, decimal Amount) : Mediant.Abstractions.ICommand<string>;
            """);

    [Fact]
    public Task GOLDPATH1004_quiet_on_unmarked_commands()
        => Verify<IdempotentKeyAnalyzer>("""
            public record RecalculatePortfolio(string Currency) : Mediant.Abstractions.ICommand<string>;
            """);
}
