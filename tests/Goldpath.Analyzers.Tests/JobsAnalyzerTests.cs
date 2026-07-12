using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Goldpath.Analyzers.Tests;

public class JobsAnalyzerTests
{
    // Hermetic stubs: the rules match Goldpath.IGoldpathJob / Goldpath.GoldpathJobsOptions by metadata name.
    private const string Stubs = """
        namespace Goldpath
        {
            public interface IGoldpathJob
            {
                System.Threading.Tasks.Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, System.Threading.CancellationToken ct);
                System.Threading.Tasks.Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, System.Threading.CancellationToken ct);
            }
            public sealed class GoldpathJobPlan { }
            public sealed class GoldpathJobChunk { }
            public sealed class GoldpathJobContext { }
            public sealed class GoldpathJobsOptions
            {
                public GoldpathJobsOptions AddJob<TJob>(System.Action<GoldpathJobBuilder<TJob>>? configure = null) where TJob : class, IGoldpathJob => this;
            }
            public sealed class GoldpathJobBuilder<TJob>
            {
                public System.TimeSpan? Deadline { get; set; }
                public string? Cron { get; set; }
            }
            public static class Db
            {
                public static object BeginTransaction(this object database) => database;
                public static System.Threading.Tasks.Task BeginTransactionAsync(this object database) => System.Threading.Tasks.Task.CompletedTask;
                public static System.Collections.Generic.List<T> ToList<T>(this System.Collections.Generic.IEnumerable<T> source) => new();
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

    private const string JobShell = """
        using Goldpath;
        public sealed class NightlyJob : IGoldpathJob
        {
            private readonly object _database = new();
            public System.Threading.Tasks.Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, System.Threading.CancellationToken ct)
                {PLAN}
            public System.Threading.Tasks.Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, System.Threading.CancellationToken ct)
                {CHUNK}
        }
        """;

    private static string Job(string plan = "=> System.Threading.Tasks.Task.FromResult(new GoldpathJobPlan());",
        string chunk = "=> System.Threading.Tasks.Task.CompletedTask;")
        => JobShell.Replace("{PLAN}", plan).Replace("{CHUNK}", chunk);

    [Fact]
    public Task GOLDPATH1301_flags_a_transaction_inside_the_chunk()
        => Verify<ChunkTransactionAnalyzer>(
            Job(chunk: "{ var tx = {|#0:_database.BeginTransaction()|}; return System.Threading.Tasks.Task.CompletedTask; }"),
            new DiagnosticResult(Descriptors.ChunkOwnTransaction).WithLocation(0));

    [Fact]
    public Task GOLDPATH1301_ignores_transactions_outside_goldpath_jobs()
        => Verify<ChunkTransactionAnalyzer>("""
            using Goldpath;
            public class NotAJob
            {
                private readonly object _database = new();
                public void ExecuteChunkAsync() { _ = _database.BeginTransaction(); }
            }
            """);

    [Fact]
    public Task GOLDPATH1302_flags_addjob_without_deadline()
        => Verify<JobDeadlineAnalyzer>(
            Job() + """
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathJobsOptions options)
                    => {|#0:options.AddJob<NightlyJob>(j => j.Cron = "0 0 1 * * ?")|};
            }
            """,
            new DiagnosticResult(Descriptors.JobWithoutDeadline).WithLocation(0).WithArguments("NightlyJob"));

    [Fact]
    public Task GOLDPATH1302_accepts_a_deadline()
        => Verify<JobDeadlineAnalyzer>(
            Job() + """
            public static class Composition
            {
                public static void Wire(Goldpath.GoldpathJobsOptions options)
                    => options.AddJob<NightlyJob>(j => j.Deadline = System.TimeSpan.FromHours(5));
            }
            """);

    [Fact]
    public Task GOLDPATH1303_flags_materializing_plans()
        => Verify<PlanMaterializationAnalyzer>(
            Job(plan: """
                {
                    var items = {|#0:System.Linq.Enumerable.Range(0, 10).ToList()|};
                    return System.Threading.Tasks.Task.FromResult(new GoldpathJobPlan());
                }
                """),
            new DiagnosticResult(Descriptors.PlanMaterializesItems).WithLocation(0).WithArguments("ToList"));

    [Fact]
    public Task GOLDPATH1303_ignores_materialization_outside_plans()
        => Verify<PlanMaterializationAnalyzer>(
            Job(chunk: """
                {
                    var slice = System.Linq.Enumerable.Range(0, 10).ToList();
                    return System.Threading.Tasks.Task.CompletedTask;
                }
                """));
}
