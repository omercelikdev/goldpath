using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Goldpath.Analyzers.Tests;

public class MigrationsAnalyzerTests
{
    private const string Stubs = """
        namespace Goldpath
        {
            public static class ModelExtensions
            {
                public static object AddGoldpathJobs(this object modelBuilder, bool excludeFromMigrations = false, bool someOtherFlag = false) => modelBuilder;
            }
        }
        """;

    private static Task Verify(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<SharedTablesOwnershipAnalyzer, DefaultVerifier>
        {
            TestCode = source + "\n" + Stubs,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    [Fact]
    public Task GP1801_flags_a_second_owning_context_in_the_same_assembly()
        => Verify("""
            using Goldpath;
            public class ApiDb { public void OnModelCreating(object b) => b.AddGoldpathJobs(); }
            public class WorkerDb { public void OnModelCreating(object b) => {|#0:b.AddGoldpathJobs()|}; }
            """,
            new DiagnosticResult(Descriptors.SharedTablesDoubleOwnership).WithLocation(0)
                .WithArguments("WorkerDb", "AddGoldpathJobs", "ApiDb"));

    [Fact]
    public Task GP1801_is_silent_when_the_second_context_excludes()
        => Verify("""
            using Goldpath;
            public class ApiDb { public void OnModelCreating(object b) => b.AddGoldpathJobs(); }
            public class WorkerDb { public void OnModelCreating(object b) => b.AddGoldpathJobs(excludeFromMigrations: true); }
            """);

    [Fact]
    public Task GP1801_still_fires_when_an_unrelated_true_is_passed()
    {
        // The review agent's PR #1 finding: exclusion must match by NAME, not by any
        // true literal — an unrelated named true is NOT an ownership opt-out.
        return Verify("""
            using Goldpath;
            public class ApiDb { public void OnModelCreating(object b) => b.AddGoldpathJobs(); }
            public class WorkerDb { public void OnModelCreating(object b) => {|#0:b.AddGoldpathJobs(someOtherFlag: true)|}; }
            """,
            new DiagnosticResult(Descriptors.SharedTablesDoubleOwnership).WithLocation(0)
                .WithArguments("WorkerDb", "AddGoldpathJobs", "ApiDb"));
    }

    [Fact]
    public Task GP1801_accepts_the_lone_positional_true()
        => Verify("""
            using Goldpath;
            public class ApiDb { public void OnModelCreating(object b) => b.AddGoldpathJobs(); }
            public class WorkerDb { public void OnModelCreating(object b) => b.AddGoldpathJobs(true); }
            """);

    [Fact]
    public Task GP1801_is_silent_for_a_single_owner()
        => Verify("""
            using Goldpath;
            public class ApiDb { public void OnModelCreating(object b) => b.AddGoldpathJobs(); }
            """);
}
