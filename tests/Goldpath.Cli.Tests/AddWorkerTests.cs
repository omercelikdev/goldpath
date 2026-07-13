using Xunit;

namespace Goldpath.Cli.Tests;

public class AddWorkerTests
{
    private static int Add(string name, string trigger, FakeApp app, FakeProcessRunner runner)
        => CliRunner.Run(["add", "worker", name, "--trigger", trigger, "--path", app.Root],
            runner, TextWriter.Null, TextWriter.Null);

    [Fact]
    public void Queue_worker_lands_with_project_sln_and_apphost_chain()
    {
        using var app = new FakeApp(messagingWired: true);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Add("payments", "queue", app, runner));

        var projectDir = Path.Combine(app.Root, "src", "Shop.PaymentsWorker");
        Assert.True(File.Exists(Path.Combine(projectDir, "Shop.PaymentsWorker.csproj")));
        Assert.True(File.Exists(Path.Combine(projectDir, "Program.cs")));
        // Aspire endpoint inference: no launchSettings means WithHttpHealthCheck kills the AppHost.
        Assert.Contains("applicationUrl", File.ReadAllText(Path.Combine(projectDir, "Properties", "launchSettings.json")));
        Assert.True(File.Exists(Path.Combine(projectDir, "WorkItems", "WorkItemQueuedConsumer.cs")));
        Assert.Equal("global using Goldpath;\n", File.ReadAllText(Path.Combine(projectDir, "GlobalUsings.cs")));   // Goldpath types resolve

        var program = File.ReadAllText(Path.Combine(projectDir, "Program.cs"));
        Assert.Contains("GetConnectionString(\"shopdb\")", program, StringComparison.Ordinal);   // the connection CHAIN
        Assert.Contains("bus.AddGoldpathOutbox<WorkDbContext>(outbox => outbox.UsePostgres());", program, StringComparison.Ordinal);
        Assert.Contains("namespace Shop.PaymentsWorker.WorkItems;",
            File.ReadAllText(Path.Combine(projectDir, "WorkItems", "WorkDbContext.cs")), StringComparison.Ordinal);

        var appHost = app.Read(app.AppHost);
        Assert.Contains("builder.AddProject<Projects.Shop_PaymentsWorker>(\"payments-worker\")", appHost, StringComparison.Ordinal);
        Assert.Contains("    .WithReference(messaging).WaitFor(messaging)", appHost, StringComparison.Ordinal);
        Assert.Contains("<ProjectReference Include=\"../Shop.PaymentsWorker/Shop.PaymentsWorker.csproj\" />",
            app.Read(app.AppHostProject), StringComparison.Ordinal);

        var sln = Assert.Single(runner.Calls, c => c.Arguments.Contains("sln"));
        Assert.Contains("add", sln.Arguments);
        Assert.Contains(sln.Arguments, a => a.EndsWith("Shop.PaymentsWorker.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void Jobs_worker_runs_its_own_fleet_against_the_app_database()
    {
        using var app = new FakeApp();
        Assert.Equal(0, Add("eod-report", "jobs", app, new FakeProcessRunner()));

        var projectDir = Path.Combine(app.Root, "src", "Shop.EodReportWorker");
        var program = File.ReadAllText(Path.Combine(projectDir, "Program.cs"));
        Assert.Contains("jobs.ConnectionName = \"shopdb\";", program, StringComparison.Ordinal);
        Assert.Contains("jobs.SchedulerName = \"shop-eodreportworker\";", program, StringComparison.Ordinal);   // its OWN fleet
        Assert.Contains("app.MapGoldpathJobsAdmin<ReportsDbContext>(exposeUnsecured: true);", program, StringComparison.Ordinal);   // the H2 opt-out is WRITTEN, not implied
        Assert.True(File.Exists(Path.Combine(projectDir, "Reports", "NightlyReportJob.cs")));

        var appHost = app.Read(app.AppHost);
        Assert.Contains("builder.AddProject<Projects.Shop_EodReportWorker>(\"eod-report-worker\")", appHost, StringComparison.Ordinal);
        Assert.Contains("    .WithReference(database).WaitFor(database)", appHost.Split("Shop_EodReportWorker")[1], StringComparison.Ordinal);
        Assert.DoesNotContain("messaging", appHost.Split("Shop_EodReportWorker")[1].Split(".WithHttpHealthCheck")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Schedule_worker_needs_no_database_or_broker()
    {
        using var app = new FakeApp();
        Assert.Equal(0, Add("cleanup", "schedule", app, new FakeProcessRunner()));

        var projectDir = Path.Combine(app.Root, "src", "Shop.CleanupWorker");
        Assert.True(File.Exists(Path.Combine(projectDir, "Jobs", "IntervalJob.cs")));
        var csproj = File.ReadAllText(Path.Combine(projectDir, "Shop.CleanupWorker.csproj"));
        Assert.DoesNotContain("Goldpath.Data", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("Goldpath.Messaging", csproj, StringComparison.Ordinal);

        var wiring = app.Read(app.AppHost).Split("Shop_CleanupWorker")[1].Split(";")[0];
        Assert.DoesNotContain("WithReference", wiring, StringComparison.Ordinal);   // probes only
    }

    [Fact]
    public void Queue_worker_without_messaging_is_refused_before_anything_is_written()
    {
        using var app = new FakeApp();
        Assert.NotEqual(0, Add("payments", "queue", app, new FakeProcessRunner()));
        Assert.False(Directory.Exists(Path.Combine(app.Root, "src", "Shop.PaymentsWorker")));
        Assert.DoesNotContain("PaymentsWorker", app.Read(app.AppHost), StringComparison.Ordinal);
    }

    [Fact]
    public void A_red_engine_restores_everything_including_the_new_project()
    {
        using var app = new FakeApp(messagingWired: true);
        var appHostBefore = app.Read(app.AppHost);
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["validate"] = 1;

        Assert.Equal(1, Add("payments", "queue", app, runner));
        Assert.False(Directory.Exists(Path.Combine(app.Root, "src", "Shop.PaymentsWorker")));
        Assert.Equal(appHostBefore, app.Read(app.AppHost));
    }

    [Fact]
    public void A_second_worker_with_the_same_name_is_refused()
    {
        using var app = new FakeApp(messagingWired: true);
        Assert.Equal(0, Add("payments", "queue", app, new FakeProcessRunner()));
        Assert.NotEqual(0, Add("payments", "queue", app, new FakeProcessRunner()));
    }

    [Fact]
    public void An_unknown_trigger_is_a_usage_error()
    {
        using var app = new FakeApp();
        Assert.Equal(2, Add("payments", "cron", app, new FakeProcessRunner()));
    }

    [Fact]
    public void A_worker_manifest_refuses_the_verb()
    {
        using var app = new FakeApp(kind: "worker");
        Assert.NotEqual(0, Add("payments", "schedule", app, new FakeProcessRunner()));
    }

    [Fact]
    public void Sqlserver_solutions_generate_the_sqlserver_chain()
    {
        using var app = new FakeApp(sqlServer: true, messagingWired: true);
        Assert.Equal(0, Add("payments", "queue", app, new FakeProcessRunner()));
        var projectDir = Path.Combine(app.Root, "src", "Shop.PaymentsWorker");
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer",
            File.ReadAllText(Path.Combine(projectDir, "Shop.PaymentsWorker.csproj")), StringComparison.Ordinal);
        var program = File.ReadAllText(Path.Combine(projectDir, "Program.cs"));
        Assert.Contains("options.UseSqlServer(workDbConnection);", program, StringComparison.Ordinal);
        Assert.Contains("outbox.UseSqlServer()", program, StringComparison.Ordinal);
    }
}
