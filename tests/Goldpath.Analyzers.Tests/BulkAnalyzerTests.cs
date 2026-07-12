using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Goldpath.Analyzers.Tests;

public class BulkAnalyzerTests
{
    // Hermetic stubs: rules match Goldpath.GoldpathBulkOptions / Goldpath.IGoldpathBulkRowHandler`1 by metadata name.
    private const string Stubs = """
        namespace Goldpath
        {
            public sealed class GoldpathBulkOptions
            {
                public GoldpathBulkOptions AddBatch<TRow>(string name, System.Action<GoldpathBulkBatchBuilder<TRow>> configure) where TRow : class, new() => this;
            }
            public sealed class GoldpathBulkBatchBuilder<TRow>
            {
                public GoldpathBulkBatchBuilder<TRow> MaxRows(int maxRows) => this;
                public GoldpathBulkBatchBuilder<TRow> AutoApprove() => this;
                public GoldpathBulkBatchBuilder<TRow> RowKey(System.Func<TRow, string> key) => this;
            }
            public sealed class GoldpathBulkRowContext { }
            public interface IGoldpathBulkRowHandler<in TRow> where TRow : class
            {
                System.Threading.Tasks.Task ExecuteAsync(TRow row, GoldpathBulkRowContext context, System.Threading.CancellationToken cancellationToken);
            }
        }
        public sealed class ImportRow { public string Code { get; set; } = ""; }
        public sealed class FakeDb
        {
            public int SaveChanges() => 0;
            public System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.FromResult(0);
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

    [Fact]
    public Task GOLDPATH1501_flags_a_definition_without_a_ceiling()
        => Verify<BulkCeilingAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathBulkOptions options)
                    => {|#0:options.AddBatch<ImportRow>("imports", b => b.RowKey(r => r.Code))|};
            }
            """,
            new DiagnosticResult(Descriptors.BulkBatchWithoutCeiling).WithLocation(0).WithArguments("ImportRow"));

    [Fact]
    public Task GOLDPATH1501_is_silent_when_the_ceiling_is_declared()
        => Verify<BulkCeilingAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathBulkOptions options)
                    => options.AddBatch<ImportRow>("imports", b => b.MaxRows(50000));
            }
            """);

    [Fact]
    public Task GOLDPATH1502_flags_SaveChanges_inside_a_row_handler()
        => Verify<BulkHandlerSaveAnalyzer>("""
            public sealed class ImportHandler : Goldpath.IGoldpathBulkRowHandler<ImportRow>
            {
                private readonly FakeDb _db = new();
                public async System.Threading.Tasks.Task ExecuteAsync(ImportRow row, Goldpath.GoldpathBulkRowContext context, System.Threading.CancellationToken cancellationToken)
                    => await {|#0:_db.SaveChangesAsync(cancellationToken)|};
            }
            """,
            new DiagnosticResult(Descriptors.BulkHandlerSavesPerRow).WithLocation(0).WithArguments("ImportHandler"));

    [Fact]
    public Task GOLDPATH1502_is_silent_outside_row_handlers()
        => Verify<BulkHandlerSaveAnalyzer>("""
            public sealed class SomeService
            {
                private readonly FakeDb _db = new();
                public void Flush() => _db.SaveChanges();
            }
            """);

    [Fact]
    public Task GOLDPATH1503_makes_the_skipped_gate_visible()
        => Verify<BulkAutoApproveAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathBulkOptions options)
                    => {|#0:options.AddBatch<ImportRow>("imports", b => b.MaxRows(1000).AutoApprove())|};
            }
            """,
            new DiagnosticResult(Descriptors.BulkAutoApprove).WithLocation(0).WithArguments("ImportRow"));

    [Fact]
    public Task GOLDPATH1503_is_silent_for_gated_definitions()
        => Verify<BulkAutoApproveAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathBulkOptions options)
                    => options.AddBatch<ImportRow>("imports", b => b.MaxRows(1000));
            }
            """);
}
