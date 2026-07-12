using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Goldpath.Analyzers.Tests;

public class ArchivalAnalyzerTests
{
    // Hermetic stubs: rules match Goldpath.GoldpathArchivalOptions / GoldpathPersonalDataAttribute /
    // GoldpathDataProtectionExtensions by metadata name.
    private const string Stubs = """
        namespace Goldpath
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class GoldpathPersonalDataAttribute : System.Attribute { }
            public sealed class GoldpathArchivalOptions
            {
                public GoldpathArchivalOptions AddArchive<TRoot>(System.Action<GoldpathArchiveBuilder<TRoot>> configure) where TRoot : class => this;
                public GoldpathArchivalOptions AddRowRetention<TRow>(System.Action<GoldpathRowRetentionBuilder<TRow>> configure) where TRow : class => this;
            }
            public sealed class GoldpathArchiveBuilder<TRoot>
            {
                public GoldpathArchiveBuilder<TRoot> Key<TKey>(System.Func<TRoot, TKey> key) => this;
                public GoldpathArchiveBuilder<TRoot> DueWhen(System.Func<TRoot, bool> due, System.Func<TRoot, System.DateTimeOffset> dueAt) => this;
            }
            public sealed class GoldpathRowRetentionBuilder<TRow>
            {
                public GoldpathRowRetentionBuilder<TRow> After(System.TimeSpan period, System.Func<TRow, System.DateTimeOffset> age) => this;
                public GoldpathRowRetentionBuilder<TRow> Where(System.Func<TRow, bool> guard) => this;
            }
        }
        """;

    private const string ModulePresent = """
        namespace Goldpath { public static class GoldpathDataProtectionExtensions { } }
        """;

    private const string Domain = """
        public sealed class ClaimFile
        {
            public System.Guid Id { get; set; }
            public System.DateTimeOffset? ClosedAt { get; set; }
            public System.Collections.Generic.List<Treatment> Treatments { get; set; } = new();
        }
        public sealed class Treatment
        {
            [Goldpath.GoldpathPersonalData]
            public string Diagnosis { get; set; } = "";
        }
        public sealed class UsageRow
        {
            public System.DateTimeOffset RecordedAt { get; set; }
            public bool RolledUp { get; set; }
        }
        """;

    private static Task Verify<TAnalyzer>(string source, params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source + "\n" + Stubs + "\n" + Domain,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    [Fact]
    public Task GOLDPATH1401_flags_a_classified_graph_without_the_module()
        => Verify<ArchiveWithoutDataProtectionAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathArchivalOptions options)
                    => {|#0:options.AddArchive<ClaimFile>(a => a.DueWhen(c => c.ClosedAt != null, c => c.ClosedAt!.Value))|};
            }
            """,
            new DiagnosticResult(Descriptors.ArchiveWithoutDataProtection).WithLocation(0).WithArguments("ClaimFile"));

    [Fact]
    public Task GOLDPATH1401_is_silent_when_the_module_is_referenced()
        => Verify<ArchiveWithoutDataProtectionAnalyzer>(ModulePresent + """
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathArchivalOptions options)
                    => options.AddArchive<ClaimFile>(a => a.DueWhen(c => c.ClosedAt != null, c => c.ClosedAt!.Value));
            }
            """);

    [Fact]
    public Task GOLDPATH1401_is_silent_for_unclassified_graphs()
        => Verify<ArchiveWithoutDataProtectionAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathArchivalOptions options)
                    => options.AddArchive<UsageRow>(a => a.DueWhen(u => u.RolledUp, u => u.RecordedAt));
            }
            """);

    [Fact]
    public Task GOLDPATH1402_flags_guardless_row_retention()
        => Verify<RowRetentionGuardAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathArchivalOptions options)
                    => {|#0:options.AddRowRetention<UsageRow>(r => r.After(System.TimeSpan.FromDays(90), u => u.RecordedAt))|};
            }
            """,
            new DiagnosticResult(Descriptors.RowRetentionWithoutGuard).WithLocation(0).WithArguments("UsageRow"));

    [Fact]
    public Task GOLDPATH1402_accepts_a_guard()
        => Verify<RowRetentionGuardAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathArchivalOptions options)
                    => options.AddRowRetention<UsageRow>(r => r.After(System.TimeSpan.FromDays(90), u => u.RecordedAt).Where(u => u.RolledUp));
            }
            """);

    [Fact]
    public Task GOLDPATH1403_flags_a_missing_lifecycle()
        => Verify<ArchiveLifecycleAnalyzer>("""
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathArchivalOptions options)
                    => {|#0:options.AddArchive<UsageRow>(a => a.Key(u => u.RecordedAt))|};
            }
            """,
            new DiagnosticResult(Descriptors.ArchiveWithoutLifecycle).WithLocation(0).WithArguments("UsageRow"));
}
