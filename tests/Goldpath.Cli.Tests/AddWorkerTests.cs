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
        using var app = new FakeApp(jobsWired: true);
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

    [Fact]
    public void An_authed_solution_gets_an_authed_worker_head()
    {
        using var app = new FakeApp(jobsWired: true, authWired: true);
        Assert.Equal(0, Add("eod-report", "jobs", app, new FakeProcessRunner()));

        var projectDir = Path.Combine(app.Root, "src", "Shop.EodReportWorker");
        var program = File.ReadAllText(Path.Combine(projectDir, "Program.cs"));
        Assert.Contains("builder.AddGoldpathAuth();", program, StringComparison.Ordinal);
        Assert.Contains("app.UseGoldpathAuth();", program, StringComparison.Ordinal);
        // The floor is UP: no visible opt-out on the fleet's admin surface.
        Assert.Contains("app.MapGoldpathJobsAdmin<ReportsDbContext>();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("exposeUnsecured", program, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Goldpath.Auth\" />", File.ReadAllText(Path.Combine(projectDir, "Shop.EodReportWorker.csproj")), StringComparison.Ordinal);
        // Middleware order: auth AFTER build, BEFORE the probes are mapped.
        Assert.True(program.IndexOf("app.UseGoldpathAuth();", StringComparison.Ordinal) < program.IndexOf("app.MapGoldpathDefaultEndpoints();", StringComparison.Ordinal));
    }

    [Fact]
    public void An_api_key_solution_gives_the_worker_the_api_key_strategy()
    {
        using var app = new FakeApp(jobsWired: true, authWired: true, apiKey: true);
        Assert.Equal(0, Add("eod-report", "jobs", app, new FakeProcessRunner()));
        var program = File.ReadAllText(Path.Combine(app.Root, "src", "Shop.EodReportWorker", "Program.cs"));
        Assert.Contains("builder.AddGoldpathAuth(o => o.Strategy = GoldpathAuthStrategy.ApiKey);", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_solutions_features_ride_along_on_the_workers_own_context()
    {
        using var app = new FakeApp(messagingWired: true, auditTrailWired: true, softDeleteWired: true, multiTenancyWired: true, dataProtectionWired: true, lockingWired: true);
        Assert.Equal(0, Add("ingest", "queue", app, new FakeProcessRunner()));

        var projectDir = Path.Combine(app.Root, "src", "Shop.IngestWorker");
        var program = File.ReadAllText(Path.Combine(projectDir, "Program.cs"));
        Assert.Contains("builder.AddGoldpathMultiTenancy();", program, StringComparison.Ordinal);
        Assert.Contains("builder.AddGoldpathAuditTrail<WebApplicationBuilder, WorkDbContext>();", program, StringComparison.Ordinal);
        Assert.Contains("builder.AddGoldpathSoftDelete();", program, StringComparison.Ordinal);
        Assert.Contains("builder.AddGoldpathDataProtection();", program, StringComparison.Ordinal);
        Assert.Contains("o.Provider = GoldpathLockProvider.Postgres;", program, StringComparison.Ordinal);
        Assert.Contains("app.UseGoldpathMultiTenancy();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UseGoldpathAuth", program, StringComparison.Ordinal);   // no floor on this solution

        var model = File.ReadAllText(Path.Combine(projectDir, "WorkItems", "WorkDbContext.cs"));
        Assert.Contains("modelBuilder.AddGoldpathAuditLog();", model, StringComparison.Ordinal);
        Assert.Contains("modelBuilder.ApplyGoldpathSoftDelete();", model, StringComparison.Ordinal);
        Assert.Contains("modelBuilder.ApplyGoldpathMultiTenancy(this);", model, StringComparison.Ordinal);

        var csproj = File.ReadAllText(Path.Combine(projectDir, "Shop.IngestWorker.csproj"));
        foreach (var package in new[] { "Goldpath.MultiTenancy", "Goldpath.AuditTrail", "Goldpath.SoftDelete", "Goldpath.DataProtection", "Goldpath.Locking" })
        {
            Assert.Contains($"<PackageReference Include=\"{package}\" />", csproj, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_schedule_worker_takes_only_the_process_wide_features()
    {
        // No context of its own: audit/soft-delete/locking own tables and stay out; tenancy
        // and data protection are process-wide and ride along.
        using var app = new FakeApp(auditTrailWired: true, softDeleteWired: true, multiTenancyWired: true, dataProtectionWired: true, lockingWired: true);
        Assert.Equal(0, Add("tick", "schedule", app, new FakeProcessRunner()));

        var projectDir = Path.Combine(app.Root, "src", "Shop.TickWorker");
        var program = File.ReadAllText(Path.Combine(projectDir, "Program.cs"));
        Assert.Contains("builder.AddGoldpathMultiTenancy();", program, StringComparison.Ordinal);
        Assert.Contains("builder.AddGoldpathDataProtection();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddGoldpathAuditTrail", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddGoldpathSoftDelete", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddGoldpathLocking", program, StringComparison.Ordinal);
        var csproj = File.ReadAllText(Path.Combine(projectDir, "Shop.TickWorker.csproj"));
        Assert.DoesNotContain("Goldpath.AuditTrail", csproj, StringComparison.Ordinal);
        Assert.Contains("Goldpath.MultiTenancy", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Sqlserver_solutions_give_the_worker_the_sqlserver_lock_provider()
    {
        using var app = new FakeApp(jobsWired: true, sqlServer: true, lockingWired: true);
        Assert.Equal(0, Add("eod", "jobs", app, new FakeProcessRunner()));
        var projectDir = Path.Combine(app.Root, "src", "Shop.EodWorker");
        Assert.Contains("builder.AddGoldpathSqlServerLocking(o => o.ConnectionName = \"shopdb\");", File.ReadAllText(Path.Combine(projectDir, "Program.cs")), StringComparison.Ordinal);
        Assert.Contains("Goldpath.Locking.SqlServer", File.ReadAllText(Path.Combine(projectDir, "Shop.EodWorker.csproj")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_verb_after_a_worker_still_finds_the_primary_head()
    {
        // The worker is a web host too; without its marker the next add verb saw two web
        // projects and refused (GmWorkerInSolution, 2026-09-05).
        using var app = new FakeApp(jobsWired: true, messagingWired: true);
        var runner = new FakeProcessRunner();
        Assert.Equal(0, Add("eod", "jobs", app, runner));
        Assert.Equal(0, Add("ingest", "queue", app, runner));
        Assert.True(Directory.Exists(Path.Combine(app.Root, "src", "Shop.IngestWorker")));
        Assert.Equal(0, CliRunner.Run(["add", "feature", "softdelete", "--path", app.Root], runner, TextWriter.Null, TextWriter.Null));
        Assert.Contains("builder.AddGoldpathSoftDelete();", app.Read(app.Program), StringComparison.Ordinal);
    }

    [Fact]
    public void A_jobs_worker_without_a_jobs_rider_is_refused_before_anything_is_written()
    {
        // The fleet shares the app database's jobs tables, which the Api's context owns —
        // without a rider there is nothing to share (GmWorkerInSolution, 2026-09-05).
        using var app = new FakeApp();   // no AddGoldpathJobs in the composition root
        var error = new StringWriter();
        var exit = CliRunner.Run(["add", "worker", "eod", "--trigger", "jobs", "--path", app.Root], new FakeProcessRunner(), TextWriter.Null, error);
        Assert.NotEqual(0, exit);
        Assert.Contains("jobs rider", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("goldpath new worker --trigger jobs", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(app.Root, "src", "Shop.EodWorker")));
    }
}
