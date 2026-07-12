using System.Text.RegularExpressions;

namespace Goldpath.Cli;

/// <summary>Everything one feature changes in an app — the CLI mirror of the drift profile's row.</summary>
public sealed class RecipePlan
{
    /// <summary>The manifest key under <c>features:</c> (already-enabled detection).</summary>
    public required string ManifestKey { get; init; }

    /// <summary>Package references for the Api project.</summary>
    public List<string> ApiPackages { get; } = [];

    /// <summary>Package references for the AppHost project.</summary>
    public List<string> AppHostPackages { get; } = [];

    /// <summary>Builder registrations (Program.cs, registrations anchor).</summary>
    public List<string> Registrations { get; } = [];

    /// <summary>Middleware lines (Program.cs, middleware anchor — before auth by anchor position).</summary>
    public List<string> Middleware { get; } = [];

    /// <summary>Endpoint-mapping lines (Program.cs, endpoints anchor — admin surfaces, after auth).</summary>
    public List<string> Endpoints { get; } = [];

    /// <summary>
    /// Lines inserted INSIDE an existing <c>AddGoldpathJobs</c> configuration (after its
    /// ConnectionName line) — jobs-riding features compose into ONE scheduler, never two.
    /// </summary>
    public List<string> JobsOptionsLines { get; } = [];

    /// <summary>
    /// Lines inserted INSIDE an existing <c>AddGoldpathMessaging</c> configuration (after the
    /// consumers anchor) — bus-riding features register on THE app's bus, never a second one.
    /// </summary>
    public List<string> BusLines { get; } = [];

    /// <summary>Model calls (OnModelCreating, model anchor; pre-indented).</summary>
    public List<string> ModelCalls { get; } = [];

    /// <summary>AppHost resource lines (resources anchor).</summary>
    public List<string> Resources { get; } = [];

    /// <summary>AppHost reference-chain lines (references anchor; pre-indented).</summary>
    public List<string> References { get; } = [];

    /// <summary>Manifest lines under <c>features:</c> (pre-indented).</summary>
    public List<string> ManifestLines { get; } = [];

    /// <summary>Markers whose lines are removed from Program.cs (retired fallbacks).</summary>
    public List<string> RemoveFromProgram { get; } = [];

    /// <summary>Domain opt-ins the team decides — printed, never guessed.</summary>
    public List<string> NextSteps { get; } = [];
}

/// <summary>What the recipes read from the app before deciding their lines.</summary>
public sealed class AppFacts
{
    /// <summary>The DbContext class name (audit registration is generic over it).</summary>
    public required string DbContextName { get; init; }

    /// <summary>EF provider detected from the Api csproj: postgres | sqlserver | none.</summary>
    public required string DatabaseProvider { get; init; }

    /// <summary>The app's database connection name (locking reuses the app database).</summary>
    public required string? ConnectionName { get; init; }

    /// <summary>Whether AddGoldpathCaching is already wired (idempotency store decision).</summary>
    public required bool CachingWired { get; init; }

    /// <summary>Whether AddGoldpathJobs is already wired (a second jobs composition would double the scheduler).</summary>
    public required bool JobsWired { get; init; }

    /// <summary>Whether AddGoldpathMessaging is already wired (campaign requires the broker seam, RFC D8).</summary>
    public required bool MessagingWired { get; init; }

    /// <summary>Reads the context facts from the located files.</summary>
    public static AppFacts Read(AppFiles files)
    {
        var model = File.ReadAllText(files.ModelFile);
        var program = File.ReadAllText(files.ProgramFile);
        var apiProject = File.ReadAllText(files.ApiProject);

        var dbContext = Regex.Match(model, @"class\s+(\w+)");
        if (!dbContext.Success)
        {
            throw new CliFailureException($"no class declaration found in {files.ModelFile} — cannot infer the DbContext type.");
        }

        var provider = apiProject.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal) ? "postgres"
            : apiProject.Contains("Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal) ? "sqlserver"
            : "none";

        var connection = Regex.Match(program, "GetConnectionString\\(\"([^\"]+)\"\\)");

        return new AppFacts
        {
            DbContextName = dbContext.Groups[1].Value,
            DatabaseProvider = provider,
            ConnectionName = connection.Success ? connection.Groups[1].Value : null,
            CachingWired = program.Contains("AddGoldpathCaching(", StringComparison.Ordinal),
            JobsWired = program.Contains("builder.AddGoldpathJobs<", StringComparison.Ordinal),
            MessagingWired = program.Contains("builder.AddGoldpathMessaging(", StringComparison.Ordinal),
        };
    }
}

/// <summary>
/// The ten Ring B recipes. Every line mirrors what <c>dotnet new goldpath-solution --features X</c>
/// would have generated — the CLI adds nothing the template would not; specdrift stays the
/// acceptance test for both paths.
/// </summary>
public static class FeatureRecipes
{
    /// <summary>The feature names <c>goldpath add feature</c> understands.</summary>
    public static readonly IReadOnlyList<string> Names =
        ["multitenancy", "audittrail", "softdelete", "idempotency", "dataprotection", "caching", "locking", "archival", "bulk", "notification", "campaign"];

    /// <summary>Builds the plan for one feature against the app's read context.</summary>
    public static RecipePlan Build(string feature, AppFacts app)
    {
        switch (feature)
        {
            case "multitenancy":
                {
                    var plan = new RecipePlan { ManifestKey = "multiTenancy" };
                    plan.ApiPackages.Add("Goldpath.MultiTenancy");
                    plan.Registrations.Add("builder.AddGoldpathMultiTenancy();");
                    plan.Middleware.Add("app.UseGoldpathMultiTenancy();                      // resolve the tenant BEFORE auth binds to it");
                    plan.ModelCalls.Add("        modelBuilder.ApplyGoldpathMultiTenancy(this);   // context-rooted ON PURPOSE — keeps the filter live");
                    plan.ManifestLines.Add("  multiTenancy: true");
                    plan.NextSteps.Add("mark tenant-owned entities: partial class X : IMultiTenant (TenantId is filtered + write-guarded)");
                    plan.NextSteps.Add("fail-closed from now on: every request (and test) must send the Goldpath-Tenant-Id header (GoldpathHeaders.TenantId)");
                    return plan;
                }

            case "audittrail":
                {
                    var plan = new RecipePlan { ManifestKey = "auditTrail" };
                    plan.ApiPackages.Add("Goldpath.AuditTrail");
                    plan.Registrations.Add($"builder.AddGoldpathAuditTrail<WebApplicationBuilder, {app.DbContextName}>();");
                    plan.ModelCalls.Add("        modelBuilder.AddGoldpathAuditLog();");
                    plan.ManifestLines.Add("  auditTrail: true");
                    plan.NextSteps.Add("mark audited entities: partial class X : IAuditLogged (change rows commit in the same transaction)");
                    return plan;
                }

            case "softdelete":
                {
                    var plan = new RecipePlan { ManifestKey = "softDelete" };
                    plan.ApiPackages.Add("Goldpath.SoftDelete");
                    plan.Registrations.Add("builder.AddGoldpathSoftDelete();");
                    plan.ModelCalls.Add("        modelBuilder.ApplyGoldpathSoftDelete();");
                    plan.ManifestLines.Add("  softDelete: true");
                    plan.NextSteps.Add("mark soft-deletable entities: partial class X : ISoftDeletable (deletes become stamped updates)");
                    return plan;
                }

            case "idempotency":
                {
                    var plan = new RecipePlan { ManifestKey = "idempotency" };
                    plan.ApiPackages.Add("Goldpath.Idempotency");
                    if (!app.CachingWired)
                    {
                        plan.Registrations.Add("builder.Services.AddDistributedMemoryCache();  // idempotency store fallback; enable caching for Redis-backed keys");
                    }

                    plan.Registrations.Add("builder.AddGoldpathIdempotency();");
                    plan.ManifestLines.Add("  idempotency: true");
                    plan.NextSteps.Add("mark retry-sensitive commands with [Idempotent]; clients send the Idempotency-Key header");
                    return plan;
                }

            case "dataprotection":
                {
                    var plan = new RecipePlan { ManifestKey = "dataProtection" };
                    plan.ApiPackages.Add("Goldpath.DataProtection");
                    plan.Registrations.Add("builder.AddGoldpathDataProtection();");
                    plan.ManifestLines.Add("  dataProtection: true");
                    plan.NextSteps.Add("classify once: [GoldpathPersonalData] on sensitive properties — every sink (audit rows, logs) masks them");
                    return plan;
                }

            case "caching":
                {
                    var plan = new RecipePlan { ManifestKey = "distributedCaching" };
                    plan.ApiPackages.Add("Goldpath.Caching");
                    plan.AppHostPackages.Add("Aspire.Hosting.Redis");
                    plan.Registrations.Add("builder.AddGoldpathCaching();                       // HybridCache L1+L2 (redis resource in the AppHost)");
                    plan.Resources.Add("var cache = builder.AddRedis(\"redis\");");
                    plan.References.Add("    .WithReference(cache).WaitFor(cache)");
                    plan.ManifestLines.Add("  distributedCaching: true");
                    plan.RemoveFromProgram.Add("idempotency store fallback");
                    plan.NextSteps.Add("mark queries [Cacheable] and the commands that change them [InvalidatesCache] — one tag vocabulary");
                    return plan;
                }

            case "locking":
                {
                    var connection = app.ConnectionName
                        ?? throw new CliFailureException("no GetConnectionString(...) found in the composition root — locking reuses the app database and needs its connection name.");
                    var plan = new RecipePlan { ManifestKey = "distributedLocking" };
                    switch (app.DatabaseProvider)
                    {
                        case "postgres":
                            plan.ApiPackages.Add("Goldpath.Locking");
                            plan.Registrations.Add("builder.AddGoldpathLocking(o =>");
                            plan.Registrations.Add("{");
                            plan.Registrations.Add("    o.Provider = GoldpathLockProvider.Postgres;     // the lock lives in the app database — zero new infra");
                            plan.Registrations.Add($"    o.ConnectionName = \"{connection}\";");
                            plan.Registrations.Add("});");
                            break;
                        case "sqlserver":
                            plan.ApiPackages.Add("Goldpath.Locking.SqlServer");
                            plan.Registrations.Add($"builder.AddGoldpathSqlServerLocking(o => o.ConnectionName = \"{connection}\");");
                            break;
                        default:
                            throw new CliFailureException("no EF provider reference found in the Api project — locking lives in the app database and needs one.");
                    }

                    plan.ManifestLines.Add("  distributedLocking:");
                    plan.ManifestLines.Add($"    provider: {app.DatabaseProvider}");
                    plan.ManifestLines.Add($"    connectionName: {connection}");
                    plan.NextSteps.Add("take locks through IGoldpathLockFactory — never raw connections; see the Goldpath.Locking README");
                    return plan;
                }

            case "archival":
                {
                    var connection = app.ConnectionName
                        ?? throw new CliFailureException("no GetConnectionString(...) found in the composition root — the archive store lives in the app database and needs its connection name.");
                    var plan = new RecipePlan { ManifestKey = "archival" };
                    plan.ApiPackages.Add("Goldpath.Archival");
                    plan.ApiPackages.Add("Goldpath.Jobs");
                    plan.Registrations.Add($"builder.AddGoldpathJobs<WebApplicationBuilder, {app.DbContextName}>(jobs =>");
                    plan.Registrations.Add("{");
                    plan.Registrations.Add($"    jobs.ConnectionName = \"{connection}\";              // runs + schedules live in the app database");
                    if (app.DatabaseProvider == "sqlserver")
                    {
                        plan.Registrations.Add("    jobs.Provider = GoldpathJobStoreProvider.SqlServer;");
                    }

                    plan.Registrations.Add($"    jobs.AddGoldpathArchivalJobs<{app.DbContextName}>();    // archive nightly, purge chained after it, verify weekly");
                    plan.Registrations.Add("});");
                    plan.Registrations.Add($"builder.AddGoldpathArchival<WebApplicationBuilder, {app.DbContextName}>(archival =>");
                    plan.Registrations.Add("{");
                    plan.Registrations.Add("    // Declare YOUR lifecycles here (goldpath never guesses domain retention):");
                    plan.Registrations.Add("    // archival.AddArchive<Order>(a => a.Key(o => o.Id)");
                    plan.Registrations.Add("    //     .DueWhen(o => o.Status == OrderStatus.Confirmed, o => o.CreatedAt)");
                    plan.Registrations.Add("    //     .ArchiveAfter(TimeSpan.FromDays(365)).RetainFor(years: 10).DeleteHotRowsAfterArchive());");
                    plan.Registrations.Add("});");
                    plan.Endpoints.Add($"app.MapGoldpathJobsAdmin<{app.DbContextName}>();        // run console API: trigger/pause/reschedule/audit");
                    plan.Endpoints.Add($"app.MapGoldpathArchivalAdmin<{app.DbContextName}>();    // lifecycle verbs: retrieve/hold/erase/verify");
                    plan.ModelCalls.Add("        modelBuilder.AddGoldpathArchiveModel();   // archive entries + chain state + holds + erasure evidence");
                    plan.ModelCalls.Add("        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)");
                    plan.ManifestLines.Add("  archival: true");
                    plan.NextSteps.Add("declare lifecycles in AddGoldpathArchival: Graph + Key + DueWhen + ArchiveAfter + RetainFor per aggregate");
                    plan.NextSteps.Add("classified data in an archived graph needs the dataprotection feature — erasure redacts through its catalog (GP1401)");
                    plan.NextSteps.Add("put /goldpath/admin/* behind an ops-scoped policy before exposing beyond the cluster boundary");
                    return plan;
                }

            case "bulk":
                {
                    var connection = app.ConnectionName
                        ?? throw new CliFailureException("no GetConnectionString(...) found in the composition root — the bulk file store lives in the app database and needs its connection name.");
                    var plan = new RecipePlan { ManifestKey = "bulk" };
                    plan.ApiPackages.Add("Goldpath.Bulk");
                    plan.ApiPackages.Add("Goldpath.Jobs");
                    if (app.JobsWired)
                    {
                        // ONE scheduler per app: compose into the existing AddGoldpathJobs block.
                        plan.JobsOptionsLines.Add("    jobs.AddGoldpathBulkJobs<" + app.DbContextName + ">();        // validate + execute runs (upload verb fires validate immediately)");
                    }
                    else
                    {
                        plan.Registrations.Add($"builder.AddGoldpathJobs<WebApplicationBuilder, {app.DbContextName}>(jobs =>");
                        plan.Registrations.Add("{");
                        plan.Registrations.Add($"    jobs.ConnectionName = \"{connection}\";              // runs + schedules live in the app database");
                        if (app.DatabaseProvider == "sqlserver")
                        {
                            plan.Registrations.Add("    jobs.Provider = GoldpathJobStoreProvider.SqlServer;");
                        }

                        plan.Registrations.Add($"    jobs.AddGoldpathBulkJobs<{app.DbContextName}>();        // validate + execute runs (upload verb fires validate immediately)");
                        plan.Registrations.Add("});");
                    }

                    plan.Registrations.Add($"builder.AddGoldpathBulk<WebApplicationBuilder, {app.DbContextName}>(bulk =>");
                    plan.Registrations.Add("{");
                    plan.Registrations.Add("    // Declare YOUR batch shapes here (goldpath never guesses domain intake):");
                    plan.Registrations.Add("    // bulk.AddBatch<OrderImportRow>(\"orders\", b => b.MaxRows(10_000)");
                    plan.Registrations.Add("    //     .RowKey(r => r.Reference)");
                    plan.Registrations.Add("    //     .Validate((row, ctx) => { /* ctx.Fail(field, message) — value-free */ }));");
                    plan.Registrations.Add("});");
                    plan.Endpoints.Add($"app.MapGoldpathJobsAdmin<{app.DbContextName}>();        // run console API: trigger/pause/reschedule/audit");
                    plan.Endpoints.Add($"app.MapGoldpathBulkAdmin<{app.DbContextName}>();        // intake verbs: upload/report/approve/reject");
                    plan.ModelCalls.Add("        modelBuilder.AddGoldpathBulk();           // files + batches + rows + value-free report");
                    plan.ModelCalls.Add("        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)");
                    plan.ManifestLines.Add("  bulk: true");
                    plan.NextSteps.Add("declare batch shapes in AddGoldpathBulk: MaxRows (mandatory, GP1501) + RowKey + Validate per file kind");
                    plan.NextSteps.Add("register a row handler per shape: IGoldpathBulkRowHandler<TRow> — no SaveChanges inside (GP1502), the chunk batches it");
                    plan.NextSteps.Add("put /goldpath/admin/* behind an ops-scoped policy before exposing beyond the cluster boundary");
                    return plan;
                }

            case "notification":
                {
                    var connection = app.ConnectionName
                        ?? throw new CliFailureException("no GetConnectionString(...) found in the composition root — the notification evidence store lives in the app database and needs its connection name.");
                    var plan = new RecipePlan { ManifestKey = "notification" };
                    plan.ApiPackages.Add("Goldpath.Notification");
                    plan.ApiPackages.Add("Goldpath.Jobs");
                    if (app.JobsWired)
                    {
                        // ONE scheduler per app: compose into the existing AddGoldpathJobs block.
                        plan.JobsOptionsLines.Add("    jobs.AddGoldpathNotificationJobs<" + app.DbContextName + ">();   // send (frequent) + body-retention (nightly)");
                    }
                    else
                    {
                        plan.Registrations.Add($"builder.AddGoldpathJobs<WebApplicationBuilder, {app.DbContextName}>(jobs =>");
                        plan.Registrations.Add("{");
                        plan.Registrations.Add($"    jobs.ConnectionName = \"{connection}\";              // runs + schedules live in the app database");
                        if (app.DatabaseProvider == "sqlserver")
                        {
                            plan.Registrations.Add("    jobs.Provider = GoldpathJobStoreProvider.SqlServer;");
                        }

                        plan.Registrations.Add($"    jobs.AddGoldpathNotificationJobs<{app.DbContextName}>();   // send (frequent) + body-retention (nightly)");
                        plan.Registrations.Add("});");
                    }

                    plan.Registrations.Add($"builder.AddGoldpathNotification<WebApplicationBuilder, {app.DbContextName}>(notification =>");
                    plan.Registrations.Add("{");
                    plan.Registrations.Add("    // Declare YOUR templates here (code templates: PR-reviewed, hash-stamped — GP1602 wants a retention window):");
                    plan.Registrations.Add("    // notification.AddTemplate(\"order-confirmed\", t => t");
                    plan.Registrations.Add("    //     .Channel(\"email\", c => c.Subject(\"\", \"...\").Body(\"\", \"... {{Token}} ...\"))");
                    plan.Registrations.Add("    //     .DeleteBodyAfter(TimeSpan.FromDays(90)));");
                    plan.Registrations.Add("});");
                    plan.Endpoints.Add($"app.MapGoldpathJobsAdmin<{app.DbContextName}>();        // run console API: trigger/pause/reschedule/audit");
                    plan.Endpoints.Add($"app.MapGoldpathNotificationAdmin<{app.DbContextName}>();   // read-only evidence views (recipients masked)");
                    plan.ModelCalls.Add("        modelBuilder.AddGoldpathNotification();   // evidence rows + attachments");
                    plan.ModelCalls.Add("        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)");
                    plan.ManifestLines.Add("  notification: true");
                    plan.NextSteps.Add("declare templates in AddGoldpathNotification (code, per channel per culture; DeleteBodyAfter is GP1602's ask)");
                    plan.NextSteps.Add("request through IGoldpathNotifier with a UNIQUE dedupKey — direct SmtpClient is GP1601-flagged (evidence hole)");
                    plan.NextSteps.Add("configure the channel: Goldpath:Notification:Email { Host, Port, UseSsl, User, Password, From }");
                    return plan;
                }

            case "campaign":
                {
                    var connection = app.ConnectionName
                        ?? throw new CliFailureException("no GetConnectionString(...) found in the composition root — the campaign plan lives in the app database and needs its connection name.");
                    if (!app.MessagingWired)
                    {
                        // The schema's cross-field rule, said at CLI time: no broker, no campaign.
                        throw new CliFailureException(
                            "features.campaign REQUIRES a broker (campaign RFC D8) — no AddGoldpathMessaging(...) found in the composition root. The release path IS broker fan-out: wire messaging first (a broker resource + the AddGoldpathMessaging block), then re-run.");
                    }

                    var plan = new RecipePlan { ManifestKey = "campaign" };
                    plan.ApiPackages.Add("Goldpath.Campaign");
                    plan.ApiPackages.Add("Goldpath.Jobs");
                    if (app.JobsWired)
                    {
                        // ONE scheduler per app: compose into the existing AddGoldpathJobs block.
                        plan.JobsOptionsLines.Add("    jobs.AddGoldpathCampaignJobs<" + app.DbContextName + ">();       // pacer: the cron guarantees a LEADER exists; pacing is in-memory ticks");
                    }
                    else
                    {
                        plan.Registrations.Add($"builder.AddGoldpathJobs<WebApplicationBuilder, {app.DbContextName}>(jobs =>");
                        plan.Registrations.Add("{");
                        plan.Registrations.Add($"    jobs.ConnectionName = \"{connection}\";              // runs + schedules live in the app database");
                        if (app.DatabaseProvider == "sqlserver")
                        {
                            plan.Registrations.Add("    jobs.Provider = GoldpathJobStoreProvider.SqlServer;");
                        }

                        plan.Registrations.Add($"    jobs.AddGoldpathCampaignJobs<{app.DbContextName}>();       // pacer: the cron guarantees a LEADER exists; pacing is in-memory ticks");
                        plan.Registrations.Add("});");
                    }

                    plan.Registrations.Add($"builder.AddGoldpathCampaign<WebApplicationBuilder, {app.DbContextName}>(campaign =>");
                    plan.Registrations.Add("{");
                    plan.Registrations.Add("    // Declare YOUR campaign types here (code, PR-reviewed; operators create INSTANCES via the admin API):");
                    plan.Registrations.Add("    // campaign.AddCampaign<YourTarget>(\"your-campaign\", c => c");
                    plan.Registrations.Add("    //     .MaxTargets(1_000_000)                    // mandatory — GP1701");
                    plan.Registrations.Add("    //     .Targets((services, parameters) => /* keyset-ORDERED IAsyncEnumerable */)");
                    plan.Registrations.Add("    //     .DefaultPolicy(p => p with { Tps = 50, MaxInFlight = 1_000 }));");
                    plan.Registrations.Add("});");
                    plan.BusLines.Add($"    bus.AddGoldpathCampaignConsumers<{app.DbContextName}>();    // claim-before-execute item consumer + batching outcome sink");
                    plan.Endpoints.Add($"app.MapGoldpathJobsAdmin<{app.DbContextName}>();        // run console API: trigger/pause/reschedule/audit");
                    plan.Endpoints.Add($"app.MapGoldpathCampaignAdmin<{app.DbContextName}>();       // audited verbs: create/pause/resume/abort/throttle");
                    plan.ModelCalls.Add("        modelBuilder.AddGoldpathCampaign();       // campaigns + items (the 30M-row table) + verb audit");
                    plan.ModelCalls.Add("        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)");
                    plan.ManifestLines.Add("  campaign: true");
                    plan.NextSteps.Add("declare campaign types in AddGoldpathCampaign: MaxTargets (mandatory, GP1701) + a keyset-ORDERED Targets stream + DefaultPolicy");
                    plan.NextSteps.Add("register an item handler per type: IGoldpathCampaignItemHandler<TTarget> — no SaveChanges inside (GP1702), outcomes ride the sink");
                    plan.NextSteps.Add("operators launch instances via POST /goldpath/admin/campaign (audited); throttle is LIVE — no restart to slow a screaming gateway");
                    plan.NextSteps.Add("put /goldpath/admin/* behind an ops-scoped policy before exposing beyond the cluster boundary");
                    return plan;
                }

            default:
                throw new CliUsageException($"unknown feature '{feature}' — one of: {string.Join(", ", Names)}");
        }
    }
}
