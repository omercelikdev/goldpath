using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Goldpath.Analyzers.Tests;

/// <summary>
/// The two newest modules had NO analyzer rules at all until the preview.8 coverage audit
/// (2026-09-05) — every other Ring B feature carries one to four. These are the four.
/// </summary>
public class ApprovalsAndFileExchangeAnalyzerTests
{
    // Hermetic stubs: the rules match Goldpath.GoldpathApprovalsOptions and
    // Goldpath.GoldpathFileExchangeOptions by metadata name.
    private const string Stubs = """
        namespace Goldpath
        {
            public sealed class GoldpathApprovalsOptions
            {
                public GoldpathApprovalsOptions AddLadder(string name, System.Action<GoldpathApprovalLadderBuilder> configure) => this;
            }
            public sealed class GoldpathApprovalLadderBuilder
            {
                public GoldpathApprovalLadderBuilder Rung(string role, decimal upToInclusive, System.TimeSpan within, int requiredApprovals = 1) => this;
                public GoldpathApprovalLadderBuilder TopRung(string role, System.TimeSpan within, int requiredApprovals = 1) => this;
            }
            public sealed class GoldpathFileExchangeOptions
            {
                public GoldpathFileExchangeOptions AddRail<TRow>(string name, System.Action<GoldpathFileRailBuilder<TRow>> configure) => this;
            }
            public sealed class GoldpathFileRailBuilder<TRow>
            {
                public GoldpathFileRailBuilder<TRow> Header(int lines) => this;
                public GoldpathFileRailBuilder<TRow> ParseLine(System.Func<string, TRow> parse) => this;
                public GoldpathFileRailBuilder<TRow> ValidateRow(System.Func<TRow, string?> validate) => this;
                public GoldpathFileRailBuilder<TRow> Handle(System.Func<TRow, System.Threading.CancellationToken, System.Threading.Tasks.Task> handle) => this;
            }
            public static class Composition
            {
                public static void AddGoldpathApprovals(System.Action<GoldpathApprovalsOptions> configure) => configure(new GoldpathApprovalsOptions());
                public static void AddGoldpathApprovalsJobs() { }
                public static void AddGoldpathFileExchange(System.Action<GoldpathFileExchangeOptions> configure) => configure(new GoldpathFileExchangeOptions());
            }
        }
        public sealed class RegistryRow { public decimal Amount { get; set; } }
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

    // ── GP1901: an authority chain that stops ────────────────────────────────────────
    [Fact]
    public Task GOLDPATH1901_flags_a_ladder_built_only_from_bounded_rungs()
        => Verify<ApprovalLadderAnalyzer>("""
            public static class Wiring
            {
                public static void Wire(Goldpath.GoldpathApprovalsOptions options)
                    => {|#0:options.AddLadder("credit-limit", l => l
                        .Rung("expert", 1000000m, System.TimeSpan.FromHours(8))
                        .Rung("manager", 5000000m, System.TimeSpan.FromHours(8), 2))|};
            }
            """,
            new DiagnosticResult(Descriptors.ApprovalLadderWithoutTopRung).WithLocation(0).WithArguments("credit-limit"));

    [Fact]
    public Task GOLDPATH1901_is_silent_when_the_chain_reaches_an_unbounded_top()
        => Verify<ApprovalLadderAnalyzer>("""
            public static class Wiring
            {
                public static void Wire(Goldpath.GoldpathApprovalsOptions options)
                    => options.AddLadder("credit-limit", l => l
                        .Rung("expert", 1000000m, System.TimeSpan.FromHours(8))
                        .TopRung("general-manager", System.TimeSpan.FromHours(24)));
            }
            """);

    [Fact]
    public Task GOLDPATH1901_says_nothing_about_a_configure_it_cannot_read()
        => Verify<ApprovalLadderAnalyzer>("""
            public static class Wiring
            {
                private static void Ladder(Goldpath.GoldpathApprovalLadderBuilder l) { }

                public static void Wire(Goldpath.GoldpathApprovalsOptions options)
                    => options.AddLadder("credit-limit", Ladder);
            }
            """);

    // ── GP1902: deadlines nothing enforces ───────────────────────────────────────────
    [Fact]
    public Task GOLDPATH1902_flags_approvals_composed_without_the_sweep()
        => Verify<ApprovalsEscalationAnalyzer>("""
            public static class Wiring
            {
                public static void Wire()
                    => {|#0:Goldpath.Composition.AddGoldpathApprovals(o => o.AddLadder("credit-limit", l => l.TopRung("gm", System.TimeSpan.FromHours(24))))|};
            }
            """,
            new DiagnosticResult(Descriptors.ApprovalsWithoutEscalationSweep).WithLocation(0));

    [Fact]
    public Task GOLDPATH1902_is_silent_when_the_sweep_is_scheduled()
        => Verify<ApprovalsEscalationAnalyzer>("""
            public static class Wiring
            {
                public static void Wire()
                {
                    Goldpath.Composition.AddGoldpathApprovals(o => o.AddLadder("credit-limit", l => l.TopRung("gm", System.TimeSpan.FromHours(24))));
                    Goldpath.Composition.AddGoldpathApprovalsJobs();
                }
            }
            """);

    [Fact]
    public Task GOLDPATH1902_is_silent_where_the_module_is_not_composed()
        => Verify<ApprovalsEscalationAnalyzer>("""
            public static class Wiring
            {
                public static void Wire() { }
            }
            """);

    // ── GP2101: a rail that can quarantine nothing ───────────────────────────────────
    [Fact]
    public Task GOLDPATH2101_flags_a_rail_without_a_row_contract()
        => Verify<FileRailContractAnalyzer>("""
            public static class Wiring
            {
                public static void Wire(Goldpath.GoldpathFileExchangeOptions options)
                    => {|#0:options.AddRail<RegistryRow>("registry-daily", r => r
                        .Header(1)
                        .ParseLine(line => new RegistryRow())
                        .Handle((row, ct) => System.Threading.Tasks.Task.CompletedTask))|};
            }
            """,
            new DiagnosticResult(Descriptors.FileRailWithoutRowContract).WithLocation(0).WithArguments("registry-daily"));

    [Fact]
    public Task GOLDPATH2101_is_silent_when_the_rail_declares_its_contract()
        => Verify<FileRailContractAnalyzer>("""
            public static class Wiring
            {
                public static void Wire(Goldpath.GoldpathFileExchangeOptions options)
                    => options.AddRail<RegistryRow>("registry-daily", r => r
                        .Header(1)
                        .ParseLine(line => new RegistryRow())
                        .ValidateRow(row => row.Amount > 0 ? null : "non-positive amount")
                        .Handle((row, ct) => System.Threading.Tasks.Task.CompletedTask));
            }
            """);

    // ── GP2102: the module over nothing ──────────────────────────────────────────────
    [Fact]
    public Task GOLDPATH2102_flags_the_module_composed_with_no_rail()
        => Verify<FileExchangeRailPresenceAnalyzer>("""
            public static class Wiring
            {
                public static void Wire()
                    => {|#0:Goldpath.Composition.AddGoldpathFileExchange(files => { })|};
            }
            """,
            new DiagnosticResult(Descriptors.FileExchangeWithoutRail).WithLocation(0));

    [Fact]
    public Task GOLDPATH2102_is_silent_once_a_rail_is_declared()
        => Verify<FileExchangeRailPresenceAnalyzer>("""
            public static class Wiring
            {
                public static void Wire()
                    => Goldpath.Composition.AddGoldpathFileExchange(files => files
                        .AddRail<RegistryRow>("registry-daily", r => r.ValidateRow(row => null)));
            }
            """);
}
