using Xunit;

namespace Goldpath.Cli.Tests;

/// <summary>
/// Golden tables: the EXACT lines each recipe emits. These literals are the product —
/// they must match what the template would generate character for character, so any
/// mutation of a recipe string is a real defect.
/// </summary>
public class RecipeGoldenTests
{
    private static readonly AppFacts Postgres = new()
    {
        DbContextName = "ShopDbContext",
        DatabaseProvider = "postgres",
        ConnectionName = "shopdb",
        CachingWired = false,
        JobsWired = false,
        MessagingWired = true,
    };

    [Fact]
    public void Multitenancy_plan_is_exact()
    {
        var plan = FeatureRecipes.Build("multitenancy", Postgres);
        Assert.Equal("multiTenancy", plan.ManifestKey);
        Assert.Equal(["Goldpath.MultiTenancy"], plan.ApiPackages);
        Assert.Equal(["builder.AddGoldpathMultiTenancy();"], plan.Registrations);
        Assert.Equal(["app.UseGoldpathMultiTenancy();                      // resolve the tenant BEFORE auth binds to it"], plan.Middleware);
        Assert.Equal(["        modelBuilder.ApplyGoldpathMultiTenancy(this);   // context-rooted ON PURPOSE — keeps the filter live"], plan.ModelCalls);
        Assert.Equal(["  multiTenancy: true"], plan.ManifestLines);
        Assert.Empty(plan.AppHostPackages);
        Assert.Empty(plan.Resources);
        Assert.Empty(plan.References);
        Assert.Empty(plan.RemoveFromProgram);
        Assert.Equal(2, plan.NextSteps.Count);
        Assert.Contains("IMultiTenant", plan.NextSteps[0], StringComparison.Ordinal);
        Assert.Contains("GoldpathHeaders.TenantId", plan.NextSteps[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Audittrail_plan_is_exact_and_generic_over_the_apps_dbcontext()
    {
        var plan = FeatureRecipes.Build("audittrail", Postgres);
        Assert.Equal("auditTrail", plan.ManifestKey);
        Assert.Equal(["Goldpath.AuditTrail"], plan.ApiPackages);
        Assert.Equal(["builder.AddGoldpathAuditTrail<WebApplicationBuilder, ShopDbContext>();"], plan.Registrations);
        Assert.Equal(["        modelBuilder.AddGoldpathAuditLog();"], plan.ModelCalls);
        Assert.Equal(["  auditTrail: true"], plan.ManifestLines);
        Assert.Contains("IAuditLogged", Assert.Single(plan.NextSteps), StringComparison.Ordinal);
    }

    [Fact]
    public void Softdelete_plan_is_exact()
    {
        var plan = FeatureRecipes.Build("softdelete", Postgres);
        Assert.Equal("softDelete", plan.ManifestKey);
        Assert.Equal(["Goldpath.SoftDelete"], plan.ApiPackages);
        Assert.Equal(["builder.AddGoldpathSoftDelete();"], plan.Registrations);
        Assert.Equal(["        modelBuilder.ApplyGoldpathSoftDelete();"], plan.ModelCalls);
        Assert.Equal(["  softDelete: true"], plan.ManifestLines);
        Assert.Contains("ISoftDeletable", Assert.Single(plan.NextSteps), StringComparison.Ordinal);
    }

    [Fact]
    public void Idempotency_plan_without_caching_carries_the_documented_fallback()
    {
        var plan = FeatureRecipes.Build("idempotency", Postgres);
        Assert.Equal("idempotency", plan.ManifestKey);
        Assert.Equal(["Goldpath.Idempotency"], plan.ApiPackages);
        Assert.Equal(
            [
                "builder.Services.AddDistributedMemoryCache();  // idempotency store fallback; enable caching for Redis-backed keys",
                "builder.AddGoldpathIdempotency();",
            ],
            plan.Registrations);
        Assert.Equal(["  idempotency: true"], plan.ManifestLines);
        Assert.Contains("[Idempotent]", Assert.Single(plan.NextSteps), StringComparison.Ordinal);
    }

    [Fact]
    public void Idempotency_plan_with_caching_registers_only_the_behavior()
    {
        var facts = new AppFacts { DbContextName = "X", DatabaseProvider = "postgres", ConnectionName = "db", CachingWired = true, JobsWired = false, MessagingWired = false };
        Assert.Equal(["builder.AddGoldpathIdempotency();"], FeatureRecipes.Build("idempotency", facts).Registrations);
    }

    [Fact]
    public void Dataprotection_plan_is_exact()
    {
        var plan = FeatureRecipes.Build("dataprotection", Postgres);
        Assert.Equal("dataProtection", plan.ManifestKey);
        Assert.Equal(["Goldpath.DataProtection"], plan.ApiPackages);
        Assert.Equal(["builder.AddGoldpathDataProtection();"], plan.Registrations);
        Assert.Equal(["  dataProtection: true"], plan.ManifestLines);
        Assert.Contains("[GoldpathPersonalData]", Assert.Single(plan.NextSteps), StringComparison.Ordinal);
    }

    [Fact]
    public void Caching_plan_is_exact_including_the_apphost_side()
    {
        var plan = FeatureRecipes.Build("caching", Postgres);
        Assert.Equal("distributedCaching", plan.ManifestKey);
        Assert.Equal(["Goldpath.Caching"], plan.ApiPackages);
        Assert.Equal(["Aspire.Hosting.Redis"], plan.AppHostPackages);
        Assert.Equal(["builder.AddGoldpathCaching();                       // HybridCache L1+L2 (redis resource in the AppHost)"], plan.Registrations);
        Assert.Equal(["var cache = builder.AddRedis(\"redis\");"], plan.Resources);
        Assert.Equal(["    .WithReference(cache).WaitFor(cache)"], plan.References);
        Assert.Equal(["  distributedCaching: true"], plan.ManifestLines);
        Assert.Equal(["idempotency store fallback"], plan.RemoveFromProgram);
        Assert.Contains("[Cacheable]", Assert.Single(plan.NextSteps), StringComparison.Ordinal);
    }

    [Fact]
    public void Locking_plan_on_postgres_is_exact()
    {
        var plan = FeatureRecipes.Build("locking", Postgres);
        Assert.Equal("distributedLocking", plan.ManifestKey);
        Assert.Equal(["Goldpath.Locking"], plan.ApiPackages);
        Assert.Equal(
            [
                "builder.AddGoldpathLocking(o =>",
                "{",
                "    o.Provider = GoldpathLockProvider.Postgres;     // the lock lives in the app database — zero new infra",
                "    o.ConnectionName = \"shopdb\";",
                "});",
            ],
            plan.Registrations);
        Assert.Equal(["  distributedLocking:", "    provider: postgres", "    connectionName: shopdb"], plan.ManifestLines);
        Assert.Contains("IGoldpathLockFactory", Assert.Single(plan.NextSteps), StringComparison.Ordinal);
    }

    [Fact]
    public void Locking_plan_on_sqlserver_is_exact()
    {
        var facts = new AppFacts { DbContextName = "X", DatabaseProvider = "sqlserver", ConnectionName = "shopdb", CachingWired = false, JobsWired = false, MessagingWired = false };
        var plan = FeatureRecipes.Build("locking", facts);
        Assert.Equal(["Goldpath.Locking.SqlServer"], plan.ApiPackages);
        Assert.Equal(["builder.AddGoldpathSqlServerLocking(o => o.ConnectionName = \"shopdb\");"], plan.Registrations);
        Assert.Equal(["  distributedLocking:", "    provider: sqlserver", "    connectionName: shopdb"], plan.ManifestLines);
    }

    [Fact]
    public void Locking_without_a_connection_name_or_provider_fails_with_teaching_messages()
    {
        var noConnection = new AppFacts { DbContextName = "X", DatabaseProvider = "postgres", ConnectionName = null, CachingWired = false, JobsWired = false, MessagingWired = false };
        Assert.Contains("connection name", Assert.Throws<CliFailureException>(() => FeatureRecipes.Build("locking", noConnection)).Message, StringComparison.Ordinal);

        var noProvider = new AppFacts { DbContextName = "X", DatabaseProvider = "none", ConnectionName = "db", CachingWired = false, JobsWired = false, MessagingWired = false };
        Assert.Contains("EF provider", Assert.Throws<CliFailureException>(() => FeatureRecipes.Build("locking", noProvider)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Archival_plan_on_postgres_is_exact()
    {
        var plan = FeatureRecipes.Build("archival", Postgres);
        Assert.Equal("archival", plan.ManifestKey);
        Assert.Equal(["Goldpath.Archival", "Goldpath.Jobs"], plan.ApiPackages);
        Assert.Equal(
            [
                "builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>",
                "{",
                "    jobs.ConnectionName = \"shopdb\";              // runs + schedules live in the app database",
                "    jobs.AddGoldpathArchivalJobs<ShopDbContext>();    // archive nightly, purge chained after it, verify weekly",
                "});",
                "builder.AddGoldpathArchival<WebApplicationBuilder, ShopDbContext>(archival =>",
                "{",
                "    // Declare YOUR lifecycles here (goldpath never guesses domain retention):",
                "    // archival.AddArchive<Order>(a => a.Key(o => o.Id)",
                "    //     .DueWhen(o => o.Status == OrderStatus.Confirmed, o => o.CreatedAt)",
                "    //     .ArchiveAfter(TimeSpan.FromDays(365)).RetainFor(years: 10).DeleteHotRowsAfterArchive());",
                "});",
            ],
            plan.Registrations);
        Assert.Equal(
            [
                "app.MapGoldpathJobsAdmin<ShopDbContext>();        // run console API: trigger/pause/reschedule/audit",
                "app.MapGoldpathArchivalAdmin<ShopDbContext>();    // lifecycle verbs: retrieve/hold/erase/verify",
            ],
            plan.Endpoints);
        Assert.Equal(
            [
                "        modelBuilder.AddGoldpathArchiveModel();   // archive entries + chain state + holds + erasure evidence",
                "        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)",
            ],
            plan.ModelCalls);
        Assert.Equal(["  archival: true"], plan.ManifestLines);
        Assert.Equal(3, plan.NextSteps.Count);
        Assert.Contains("GP1401", plan.NextSteps[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Archival_plan_on_sqlserver_pins_the_store_provider()
    {
        var facts = new AppFacts { DbContextName = "X", DatabaseProvider = "sqlserver", ConnectionName = "shopdb", CachingWired = false, JobsWired = false, MessagingWired = false };
        var plan = FeatureRecipes.Build("archival", facts);
        Assert.Contains("    jobs.Provider = GoldpathJobStoreProvider.SqlServer;", plan.Registrations);
    }

    [Fact]
    public void Archival_without_a_connection_name_fails_with_a_teaching_message()
    {
        var noConnection = new AppFacts { DbContextName = "X", DatabaseProvider = "postgres", ConnectionName = null, CachingWired = false, JobsWired = false, MessagingWired = false };
        Assert.Contains("connection name", Assert.Throws<CliFailureException>(() => FeatureRecipes.Build("archival", noConnection)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bulk_plan_on_a_fresh_app_opens_the_jobs_composition()
    {
        var plan = FeatureRecipes.Build("bulk", Postgres);
        Assert.Equal("bulk", plan.ManifestKey);
        Assert.Equal(["Goldpath.Bulk", "Goldpath.Jobs"], plan.ApiPackages);
        Assert.Empty(plan.JobsOptionsLines);
        Assert.Equal(
            [
                "builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>",
                "{",
                "    jobs.ConnectionName = \"shopdb\";              // runs + schedules live in the app database",
                "    jobs.AddGoldpathBulkJobs<ShopDbContext>();        // validate + execute runs (upload verb fires validate immediately)",
                "});",
                "builder.AddGoldpathBulk<WebApplicationBuilder, ShopDbContext>(bulk =>",
                "{",
                "    // Declare YOUR batch shapes here (goldpath never guesses domain intake):",
                "    // bulk.AddBatch<OrderImportRow>(\"orders\", b => b.MaxRows(10_000)",
                "    //     .RowKey(r => r.Reference)",
                "    //     .Validate((row, ctx) => { /* ctx.Fail(field, message) — value-free */ }));",
                "});",
            ],
            plan.Registrations);
        Assert.Equal(
            [
                "app.MapGoldpathJobsAdmin<ShopDbContext>();        // run console API: trigger/pause/reschedule/audit",
                "app.MapGoldpathBulkAdmin<ShopDbContext>();        // intake verbs: upload/report/approve/reject",
            ],
            plan.Endpoints);
        Assert.Equal(["  bulk: true"], plan.ManifestLines);
        Assert.Equal(3, plan.NextSteps.Count);
        Assert.Contains("GP1501", plan.NextSteps[0], StringComparison.Ordinal);
        Assert.Contains("GP1502", plan.NextSteps[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Bulk_plan_on_a_jobs_wired_app_composes_into_the_existing_scheduler()
    {
        var facts = new AppFacts { DbContextName = "ShopDbContext", DatabaseProvider = "postgres", ConnectionName = "shopdb", CachingWired = false, JobsWired = true, MessagingWired = true };
        var plan = FeatureRecipes.Build("bulk", facts);

        Assert.Equal(["    jobs.AddGoldpathBulkJobs<ShopDbContext>();        // validate + execute runs (upload verb fires validate immediately)"], plan.JobsOptionsLines);
        Assert.DoesNotContain(plan.Registrations, line => line.Contains("AddGoldpathJobs<", StringComparison.Ordinal));   // ONE scheduler per app
        Assert.Contains("builder.AddGoldpathBulk<WebApplicationBuilder, ShopDbContext>(bulk =>", plan.Registrations);
    }

    [Fact]
    public void Bulk_on_sqlserver_pins_the_store_provider_when_it_opens_the_composition()
    {
        var facts = new AppFacts { DbContextName = "X", DatabaseProvider = "sqlserver", ConnectionName = "shopdb", CachingWired = false, JobsWired = false, MessagingWired = false };
        Assert.Contains("    jobs.Provider = GoldpathJobStoreProvider.SqlServer;", FeatureRecipes.Build("bulk", facts).Registrations);
    }

    [Fact]
    public void Notification_plan_on_a_fresh_app_opens_the_jobs_composition()
    {
        var plan = FeatureRecipes.Build("notification", Postgres);
        Assert.Equal("notification", plan.ManifestKey);
        Assert.Equal(["Goldpath.Notification", "Goldpath.Jobs"], plan.ApiPackages);
        Assert.Empty(plan.JobsOptionsLines);
        Assert.Contains("builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>", plan.Registrations);
        Assert.Contains("    jobs.AddGoldpathNotificationJobs<ShopDbContext>();   // send (frequent) + body-retention (nightly)", plan.Registrations);
        Assert.Contains("builder.AddGoldpathNotification<WebApplicationBuilder, ShopDbContext>(notification =>", plan.Registrations);
        Assert.Equal(
            [
                "app.MapGoldpathJobsAdmin<ShopDbContext>();        // run console API: trigger/pause/reschedule/audit",
                "app.MapGoldpathNotificationAdmin<ShopDbContext>();   // read-only evidence views (recipients masked)",
            ],
            plan.Endpoints);
        Assert.Equal(
            [
                "        modelBuilder.AddGoldpathNotification();   // evidence rows + attachments",
                "        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)",
            ],
            plan.ModelCalls);
        Assert.Equal(["  notification: true"], plan.ManifestLines);
        Assert.Equal(3, plan.NextSteps.Count);
        Assert.Contains("GP1602", plan.NextSteps[0], StringComparison.Ordinal);
        Assert.Contains("GP1601", plan.NextSteps[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Notification_plan_on_a_jobs_wired_app_composes_into_the_existing_scheduler()
    {
        var facts = new AppFacts { DbContextName = "ShopDbContext", DatabaseProvider = "postgres", ConnectionName = "shopdb", CachingWired = false, JobsWired = true, MessagingWired = true };
        var plan = FeatureRecipes.Build("notification", facts);
        Assert.Equal(["    jobs.AddGoldpathNotificationJobs<ShopDbContext>();   // send (frequent) + body-retention (nightly)"], plan.JobsOptionsLines);
        Assert.DoesNotContain(plan.Registrations, line => line.Contains("AddGoldpathJobs<", StringComparison.Ordinal));   // ONE scheduler per app
    }

    [Fact]
    public void Campaign_plan_on_a_fresh_messaging_wired_app_opens_the_jobs_composition()
    {
        var plan = FeatureRecipes.Build("campaign", Postgres);
        Assert.Equal("campaign", plan.ManifestKey);
        Assert.Equal(["Goldpath.Campaign", "Goldpath.Jobs"], plan.ApiPackages);
        Assert.Empty(plan.JobsOptionsLines);
        Assert.Contains("builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>", plan.Registrations);
        Assert.Contains("    jobs.AddGoldpathCampaignJobs<ShopDbContext>();       // pacer: the cron guarantees a LEADER exists; pacing is in-memory ticks", plan.Registrations);
        Assert.Contains("builder.AddGoldpathCampaign<WebApplicationBuilder, ShopDbContext>(campaign =>", plan.Registrations);
        Assert.Equal(
            ["    bus.AddGoldpathCampaignConsumers<ShopDbContext>();    // claim-before-execute item consumer + batching outcome sink"],
            plan.BusLines);
        Assert.Equal(
            [
                "app.MapGoldpathJobsAdmin<ShopDbContext>();        // run console API: trigger/pause/reschedule/audit",
                "app.MapGoldpathCampaignAdmin<ShopDbContext>();       // audited verbs: create/pause/resume/abort/throttle",
            ],
            plan.Endpoints);
        Assert.Equal(
            [
                "        modelBuilder.AddGoldpathCampaign();       // campaigns + items (the 30M-row table) + verb audit",
                "        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)",
            ],
            plan.ModelCalls);
        Assert.Equal(["  campaign: true"], plan.ManifestLines);
        Assert.Equal(4, plan.NextSteps.Count);
        Assert.Contains("GP1701", plan.NextSteps[0], StringComparison.Ordinal);
        Assert.Contains("GP1702", plan.NextSteps[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Campaign_plan_on_a_jobs_wired_app_composes_into_the_existing_scheduler()
    {
        var facts = new AppFacts { DbContextName = "ShopDbContext", DatabaseProvider = "postgres", ConnectionName = "shopdb", CachingWired = false, JobsWired = true, MessagingWired = true };
        var plan = FeatureRecipes.Build("campaign", facts);
        Assert.Equal(["    jobs.AddGoldpathCampaignJobs<ShopDbContext>();       // pacer: the cron guarantees a LEADER exists; pacing is in-memory ticks"], plan.JobsOptionsLines);
        Assert.DoesNotContain(plan.Registrations, line => line.Contains("AddGoldpathJobs<", StringComparison.Ordinal));   // ONE scheduler per app
    }

    [Fact]
    public void Campaign_without_messaging_is_refused_with_the_broker_rule()
    {
        var facts = new AppFacts { DbContextName = "X", DatabaseProvider = "postgres", ConnectionName = "shopdb", CachingWired = false, JobsWired = false, MessagingWired = false };
        var e = Assert.Throws<CliFailureException>(() => FeatureRecipes.Build("campaign", facts));
        Assert.Contains("REQUIRES a broker", e.Message, StringComparison.Ordinal);
        Assert.Contains("campaign RFC D8", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Campaign_on_sqlserver_pins_the_store_provider_when_it_opens_the_composition()
    {
        var facts = new AppFacts { DbContextName = "X", DatabaseProvider = "sqlserver", ConnectionName = "shopdb", CachingWired = false, JobsWired = false, MessagingWired = true };
        Assert.Contains("    jobs.Provider = GoldpathJobStoreProvider.SqlServer;", FeatureRecipes.Build("campaign", facts).Registrations);
    }

    [Fact]
    public void The_feature_list_is_the_eleven_ring_b_features()
    {
        Assert.Equal(
            ["multitenancy", "audittrail", "softdelete", "idempotency", "dataprotection", "caching", "locking", "archival", "bulk", "notification", "campaign"],
            FeatureRecipes.Names);
    }

    [Fact]
    public void AppFacts_reads_dbcontext_provider_connection_and_caching()
    {
        using var app = new FakeApp(cachingWired: true);
        var facts = AppFacts.Read(AppFiles.Locate(app.Root));
        Assert.Equal("ShopDbContext", facts.DbContextName);
        Assert.Equal("postgres", facts.DatabaseProvider);
        Assert.Equal("shopdb", facts.ConnectionName);
        Assert.True(facts.CachingWired);

        using var sqlApp = new FakeApp(sqlServer: true);
        var sqlFacts = AppFacts.Read(AppFiles.Locate(sqlApp.Root));
        Assert.Equal("sqlserver", sqlFacts.DatabaseProvider);
        Assert.False(sqlFacts.CachingWired);
    }
}
