using Xunit;

namespace Goldpath.Cli.Tests;

/// <summary>
/// Mutation-score companions to <see cref="RecipeGoldenTests"/>: every literal a recipe emits
/// is product text (the template generates the same bytes), so each one is pinned verbatim —
/// including the teaching comments inside the registration blocks, the NextSteps prose, the
/// error messages, and both sides of every conditional a recipe decides on.
/// </summary>
public class FeatureRecipesMutationTests
{
    private static AppFacts Facts(string provider = "postgres", string? connection = "shopdb", bool caching = false, bool jobs = false, bool messaging = true, bool auth = true)
        => new()
        {
            DbContextName = "ShopDbContext",
            DatabaseProvider = provider,
            ConnectionName = connection,
            CachingWired = caching,
            JobsWired = jobs,
            MessagingWired = messaging,
            AuthWired = auth,
        };

    private static int Add(string feature, FakeApp app, FakeProcessRunner runner, TextWriter? output = null, TextWriter? error = null)
        => CliRunner.Run(["add", "feature", feature, "--path", app.Root], runner, output ?? TextWriter.Null, error ?? TextWriter.Null);

    // ---- approvals / fileexchange: the whole registration block, comments included ----

    [Fact]
    public void Approvals_plan_is_exact()
    {
        var plan = FeatureRecipes.Build("approvals", Facts());
        Assert.Equal("approvals", plan.ManifestKey);
        Assert.Equal(["Goldpath.Approvals", "Goldpath.Jobs", "Goldpath.Console"], plan.ApiPackages);
        Assert.Equal(
            [
                "builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>",
                "{",
                "    jobs.ConnectionName = \"shopdb\";              // runs + schedules live in the app database",
                "    jobs.AddGoldpathApprovalsJobs();               // escalation sweep — overdue rungs move up, the top rung expires",
                "});",
                "builder.AddGoldpathApprovals<WebApplicationBuilder, ShopDbContext>(approvals =>",
                "{",
                "    // Declare YOUR authority chains here (goldpath never guesses who may approve):",
                "    // approvals.AddLadder(\"credit-limit\", l => l",
                "    //     .Rung(\"expert\", 1_000_000m, TimeSpan.FromHours(8))",
                "    //     .Rung(\"manager\", 5_000_000m, TimeSpan.FromHours(8), requiredApprovals: 2)   // quorum is a rung property",
                "    //     .TopRung(\"general-manager\", TimeSpan.FromHours(24)));",
                "});",
            ],
            plan.Registrations);
        Assert.Equal(
            [
                "        modelBuilder.AddGoldpathApprovalModel();      // approvals + delegations + signatures (worklist survives restarts)",
                "        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)",
            ],
            plan.ModelCalls);
        Assert.Equal(["  approvals: true"], plan.ManifestLines);
        Assert.Equal(["declare ladders in AddGoldpathApprovals — the escalation sweep is already scheduled (AddGoldpathApprovalsJobs, five-minute cron)"], plan.NextSteps);
        Assert.Equal(["app.MapGoldpathJobsAdmin<ShopDbContext>();        // run console API: trigger/pause/reschedule/audit", "app.MapGoldpathConsole();                           // behind the SAME ops floor as the surfaces"], plan.Endpoints);
        Assert.Empty(plan.JobsOptionsLines);
        Assert.Empty(plan.BusLines);
    }

    [Fact]
    public void Approvals_composes_into_an_existing_jobs_block()
    {
        var plan = FeatureRecipes.Build("approvals", Facts(jobs: true));
        Assert.Equal(
            ["    jobs.AddGoldpathApprovalsJobs();               // escalation sweep — overdue rungs move up, the top rung expires"],
            plan.JobsOptionsLines);
        Assert.DoesNotContain(plan.Registrations, line => line.Contains("AddGoldpathJobs<", StringComparison.Ordinal));
    }

    [Fact]
    public void Fileexchange_plan_is_exact()
    {
        var plan = FeatureRecipes.Build("fileexchange", Facts());
        Assert.Equal("fileExchange", plan.ManifestKey);
        Assert.Equal(["Goldpath.FileExchange", "Goldpath.Jobs", "Goldpath.Console"], plan.ApiPackages);
        Assert.Equal(
            [
                "builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>",
                "{",
                "    jobs.ConnectionName = \"shopdb\";              // runs + schedules live in the app database",
                "    // your pick-up job rides here (the transport is yours — SFTP/share/object store is composed, not shipped):",
                "    // jobs.AddJob<RegistryPickupJob>(j => { j.Cron = \"0 0 6 * * ?\"; j.Deadline = TimeSpan.FromMinutes(30); });",
                "});",
                "builder.AddGoldpathFileExchange<WebApplicationBuilder, ShopDbContext>(files =>",
                "{",
                "    // Declare YOUR rails here (goldpath never guesses a counterparty format):",
                "    // files.AddRail<MyRow>(\"registry-daily\", r => r.Header(1)",
                "    //     .ParseLine(MyRow.Parse).ValidateRow(x => x.IsValid ? null : \"reason\")",
                "    //     .Handle((row, ct) => ApplyAsync(row, ct)));",
                "});",
            ],
            plan.Registrations);
        Assert.Equal(
            [
                "        modelBuilder.AddGoldpathFileExchangeModel();  // processed keys + quarantine + archive marks",
                "        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)",
            ],
            plan.ModelCalls);
        Assert.Equal(["  fileExchange: true"], plan.ManifestLines);
        Assert.Equal(["declare rails in AddGoldpathFileExchange, then write the pick-up job for your transport and hang it on the jobs block (IGoldpathJob — chunked, resumable, visible in the console)"], plan.NextSteps);
        Assert.Equal(["app.MapGoldpathJobsAdmin<ShopDbContext>();        // run console API: trigger/pause/reschedule/audit", "app.MapGoldpathConsole();                           // behind the SAME ops floor as the surfaces"], plan.Endpoints);
    }

    [Fact]
    public void Archival_composes_into_an_existing_jobs_block_instead_of_opening_a_second_scheduler()
    {
        var plan = FeatureRecipes.Build("archival", Facts(jobs: true));
        Assert.Equal(
            ["    jobs.AddGoldpathArchivalJobs<ShopDbContext>();    // archive nightly, purge chained after it, verify weekly"],
            plan.JobsOptionsLines);
        Assert.DoesNotContain(plan.Registrations, line => line.Contains("AddGoldpathJobs<", StringComparison.Ordinal));
    }

    // ---- execution-ladder modules: NextSteps prose is the operator's checklist ----

    [Fact]
    public void Archival_next_steps_are_exact()
    {
        Assert.Equal(
            [
                "declare lifecycles in AddGoldpathArchival: Graph + Key + DueWhen + ArchiveAfter + RetainFor per aggregate",
                "classified data in an archived graph needs the dataprotection feature — erasure redacts through its catalog (GP1401)",
                "put /goldpath/admin/* behind an ops-scoped policy before exposing beyond the cluster boundary",
            ],
            FeatureRecipes.Build("archival", Facts()).NextSteps);
    }

    [Fact]
    public void Bulk_next_steps_and_model_calls_are_exact()
    {
        var plan = FeatureRecipes.Build("bulk", Facts());
        Assert.Equal(
            [
                "declare batch shapes in AddGoldpathBulk: MaxRows (mandatory, GP1501) + RowKey + Validate per file kind",
                "register a row handler per shape: IGoldpathBulkRowHandler<TRow> — no SaveChanges inside (GP1502), the chunk batches it",
                "put /goldpath/admin/* behind an ops-scoped policy before exposing beyond the cluster boundary",
            ],
            plan.NextSteps);
        Assert.Equal(
            [
                "        modelBuilder.AddGoldpathBulk();           // files + batches + rows + value-free report",
                "        modelBuilder.AddGoldpathJobs();           // run model + clustered Quartz store (same database)",
            ],
            plan.ModelCalls);
    }

    [Fact]
    public void Notification_plan_on_a_fresh_postgres_app_is_exact()
    {
        var plan = FeatureRecipes.Build("notification", Facts());
        Assert.Equal(
            [
                "builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>",
                "{",
                "    jobs.ConnectionName = \"shopdb\";              // runs + schedules live in the app database",
                "    jobs.AddGoldpathNotificationJobs<ShopDbContext>();   // send (frequent) + body-retention (nightly)",
                "});",
                "builder.AddGoldpathNotification<WebApplicationBuilder, ShopDbContext>(notification =>",
                "{",
                "    // Declare YOUR templates here (code templates: PR-reviewed, hash-stamped — GP1602 wants a retention window):",
                "    // notification.AddTemplate(\"order-confirmed\", t => t",
                "    //     .Channel(\"email\", c => c.Subject(\"\", \"...\").Body(\"\", \"... {{Token}} ...\"))",
                "    //     .DeleteBodyAfter(TimeSpan.FromDays(90)));",
                "});",
            ],
            plan.Registrations);
        Assert.Equal(
            [
                "declare templates in AddGoldpathNotification (code, per channel per culture; DeleteBodyAfter is GP1602's ask)",
                "request through IGoldpathNotifier with a UNIQUE dedupKey — direct SmtpClient is GP1601-flagged (evidence hole)",
                "configure the channel: Goldpath:Notification:Email { Host, Port, UseSsl, User, Password, From }",
            ],
            plan.NextSteps);
    }

    [Fact]
    public void Notification_plan_on_a_fresh_sqlserver_app_pins_the_store_provider_in_place()
    {
        var plan = FeatureRecipes.Build("notification", Facts(provider: "sqlserver"));
        // The provider line sits between the connection name and the jobs call — position matters, not just presence.
        Assert.Equal(
            [
                "builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>",
                "{",
                "    jobs.ConnectionName = \"shopdb\";              // runs + schedules live in the app database",
                "    jobs.Provider = GoldpathJobStoreProvider.SqlServer;",
                "    jobs.AddGoldpathNotificationJobs<ShopDbContext>();   // send (frequent) + body-retention (nightly)",
                "});",
            ],
            plan.Registrations.Take(6));
    }

    [Fact]
    public void Campaign_plan_on_a_fresh_postgres_app_is_exact()
    {
        var plan = FeatureRecipes.Build("campaign", Facts());
        Assert.Equal(
            [
                "builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>",
                "{",
                "    jobs.ConnectionName = \"shopdb\";              // runs + schedules live in the app database",
                "    jobs.AddGoldpathCampaignJobs<ShopDbContext>();       // pacer: the cron guarantees a LEADER exists; pacing is in-memory ticks",
                "});",
                "builder.AddGoldpathCampaign<WebApplicationBuilder, ShopDbContext>(campaign =>",
                "{",
                "    // Declare YOUR campaign types here (code, PR-reviewed; operators create INSTANCES via the admin API):",
                "    // campaign.AddCampaign<YourTarget>(\"your-campaign\", c => c",
                "    //     .MaxTargets(1_000_000)                    // mandatory — GP1701",
                "    //     .Targets((services, parameters) => /* keyset-ORDERED IAsyncEnumerable */)",
                "    //     .DefaultPolicy(p => p with { Tps = 50, MaxInFlight = 1_000 }));",
                "});",
            ],
            plan.Registrations);
        Assert.Equal(
            [
                "declare campaign types in AddGoldpathCampaign: MaxTargets (mandatory, GP1701) + a keyset-ORDERED Targets stream + DefaultPolicy",
                "register an item handler per type: IGoldpathCampaignItemHandler<TTarget> — no SaveChanges inside (GP1702), outcomes ride the sink",
                "operators launch instances via POST /goldpath/admin/campaign (audited); throttle is LIVE — no restart to slow a screaming gateway",
                "put /goldpath/admin/* behind an ops-scoped policy before exposing beyond the cluster boundary",
            ],
            plan.NextSteps);
    }

    [Fact]
    public void Campaign_plan_on_a_fresh_sqlserver_app_pins_the_store_provider_in_place()
    {
        var plan = FeatureRecipes.Build("campaign", Facts(provider: "sqlserver"));
        Assert.Equal(
            [
                "builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>",
                "{",
                "    jobs.ConnectionName = \"shopdb\";              // runs + schedules live in the app database",
                "    jobs.Provider = GoldpathJobStoreProvider.SqlServer;",
                "    jobs.AddGoldpathCampaignJobs<ShopDbContext>();       // pacer: the cron guarantees a LEADER exists; pacing is in-memory ticks",
                "});",
            ],
            plan.Registrations.Take(6));
    }

    [Theory]
    [InlineData("archival")]
    [InlineData("bulk")]
    [InlineData("notification")]
    [InlineData("campaign")]
    public void Jobs_riding_recipes_on_postgres_never_pin_a_store_provider(string feature)
    {
        // Postgres is the default store: a provider line here would be drift against the template.
        var plan = FeatureRecipes.Build(feature, Facts(provider: "postgres"));
        Assert.DoesNotContain(plan.Registrations, line => line.Contains("jobs.Provider", StringComparison.Ordinal));
    }

    // ---- admin surfaces: the VISIBLE opt-out without auth, nothing with it ----

    [Theory]
    [InlineData("archival", "app.MapGoldpathArchivalAdmin<ShopDbContext>(exposeUnsecured: true);    // lifecycle verbs: retrieve/hold/erase/verify")]
    [InlineData("bulk", "app.MapGoldpathBulkAdmin<ShopDbContext>(exposeUnsecured: true);        // intake verbs: upload/report/approve/reject")]
    [InlineData("notification", "app.MapGoldpathNotificationAdmin<ShopDbContext>(exposeUnsecured: true);   // read-only evidence views (recipients masked)")]
    [InlineData("campaign", "app.MapGoldpathCampaignAdmin<ShopDbContext>(exposeUnsecured: true);       // audited verbs: create/pause/resume/abort/throttle")]
    public void Admin_endpoints_without_auth_carry_the_explicit_unsecured_opt_out(string feature, string moduleAdmin)
    {
        var plan = FeatureRecipes.Build(feature, Facts(auth: false));
        Assert.Equal(
            [
                "app.MapGoldpathJobsAdmin<ShopDbContext>(exposeUnsecured: true);        // run console API: trigger/pause/reschedule/audit",
                moduleAdmin,
                "app.MapGoldpathConsole(exposeUnsecured: true);      // the console over the surfaces above — visible opt-out, acceptable only behind an authenticating boundary",
            ],
            plan.Endpoints);
    }

    [Theory]
    [InlineData("archival")]
    [InlineData("bulk")]
    [InlineData("notification")]
    [InlineData("campaign")]
    public void Admin_endpoints_with_auth_take_the_policy_default(string feature)
    {
        var plan = FeatureRecipes.Build(feature, Facts(auth: true));
        Assert.Equal("app.MapGoldpathJobsAdmin<ShopDbContext>();        // run console API: trigger/pause/reschedule/audit", plan.Endpoints[0]);
        Assert.DoesNotContain(plan.Endpoints, line => line.Contains("exposeUnsecured", StringComparison.Ordinal));
    }

    // ---- the connection-name guard, per module, with its own teaching message ----

    [Theory]
    [InlineData("locking", "locking reuses the app database")]
    [InlineData("archival", "the archive store lives in the app database")]
    [InlineData("bulk", "the bulk file store lives in the app database")]
    [InlineData("notification", "the notification evidence store lives in the app database")]
    [InlineData("campaign", "the campaign plan lives in the app database")]
    public void Database_backed_recipes_without_a_connection_name_fail_with_their_own_message(string feature, string reason)
    {
        var e = Assert.Throws<CliFailureException>(() => FeatureRecipes.Build(feature, Facts(connection: null)));
        Assert.StartsWith("no GetConnectionString(...) found in the composition root — ", e.Message, StringComparison.Ordinal);
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
        Assert.EndsWith("needs its connection name.", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Campaign_checks_the_connection_name_before_the_broker_rule()
    {
        var e = Assert.Throws<CliFailureException>(() => FeatureRecipes.Build("campaign", Facts(connection: null, messaging: false)));
        Assert.Contains("campaign plan", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_feature_names_the_whole_menu()
    {
        var e = Assert.Throws<CliUsageException>(() => FeatureRecipes.Build("quantumsafe", Facts()));
        Assert.Equal(
            "unknown feature 'quantumsafe' — one of: multitenancy, audittrail, softdelete, idempotency, dataprotection, caching, locking, approvals, fileexchange, archival, bulk, notification, campaign, outbox",
            e.Message);
    }

    [Fact]
    public void Unknown_feature_through_the_cli_is_a_usage_error_with_the_menu()
    {
        using var app = new FakeApp();
        var error = new StringWriter();
        Assert.Equal(2, Add("quantumsafe", app, new FakeProcessRunner(), error: error));
        Assert.Contains("goldpath: unknown feature 'quantumsafe' — one of: multitenancy, ", error.ToString(), StringComparison.Ordinal);
    }

    // ---- AppFacts: every fact read from the app, including the absent cases ----

    [Fact]
    public void AppFacts_reports_no_provider_when_the_api_project_references_neither()
    {
        using var app = new FakeApp();
        File.WriteAllText(app.ApiProject, app.Read(app.ApiProject).Replace("Npgsql.EntityFrameworkCore.PostgreSQL", "Some.Other.Package", StringComparison.Ordinal));
        Assert.Equal("none", AppFacts.Read(AppFiles.Locate(app.Root)).DatabaseProvider);
    }

    [Fact]
    public void AppFacts_reports_a_null_connection_when_the_composition_root_has_none()
    {
        using var app = new FakeApp();
        File.WriteAllText(app.Program, app.Read(app.Program).Replace("GetConnectionString(\"shopdb\")", "GetSection(\"shopdb\").Value", StringComparison.Ordinal));
        Assert.Null(AppFacts.Read(AppFiles.Locate(app.Root)).ConnectionName);
    }

    [Fact]
    public void AppFacts_reads_jobs_messaging_and_auth_wiring()
    {
        using var app = new FakeApp(jobsWired: true, messagingWired: true, authWired: true);
        var facts = AppFacts.Read(AppFiles.Locate(app.Root));
        Assert.True(facts.JobsWired);
        Assert.True(facts.MessagingWired);
        Assert.True(facts.AuthWired);

        using var bare = new FakeApp();
        var bareFacts = AppFacts.Read(AppFiles.Locate(bare.Root));
        Assert.False(bareFacts.JobsWired);
        Assert.False(bareFacts.MessagingWired);
        Assert.False(bareFacts.AuthWired);
    }

    [Fact]
    public void AppFacts_fails_loud_when_the_model_file_declares_no_class()
    {
        using var app = new FakeApp();
        // Keep the anchor (Locate finds the file by it) but drop the class declaration.
        File.WriteAllText(app.Model, "// goldpath:features model — the drift profile is the source of these rows\n");
        var e = Assert.Throws<CliFailureException>(() => AppFacts.Read(AppFiles.Locate(app.Root)));
        Assert.Equal($"no class declaration found in {app.Model} — cannot infer the DbContext type.", e.Message);
    }

    // ---- AddFeatureCommand: guards, branches, and the rollback path ----

    [Fact]
    public void Missing_manifest_fails_with_the_path_it_looked_at()
    {
        var root = Path.Combine(Path.GetTempPath(), $"goldpath-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var error = new StringWriter();
            var exitCode = CliRunner.Run(["add", "feature", "softdelete", "--path", root], new FakeProcessRunner(), TextWriter.Null, error);
            Assert.Equal(1, exitCode);
            Assert.Contains($"no manifest at {Path.Combine(root, ".goldpath", "manifest.yaml")} — goldpath add runs inside a Goldpath-generated app (or pass --path).", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Manifest_without_a_kind_is_refused_as_none()
    {
        using var app = new FakeApp();
        File.WriteAllText(app.Manifest, app.Read(app.Manifest).Replace("kind: solution\n", string.Empty, StringComparison.Ordinal));
        var error = new StringWriter();

        Assert.Equal(1, Add("softdelete", app, new FakeProcessRunner(), error: error));
        Assert.Contains("goldpath: this manifest is kind '<none>' — Ring B features live in the owning SOLUTION's manifest; run goldpath add there.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_manifest_is_refused_naming_its_kind()
    {
        using var app = new FakeApp(kind: "worker");
        var error = new StringWriter();
        Assert.Equal(1, Add("softdelete", app, new FakeProcessRunner(), error: error));
        Assert.Contains("this manifest is kind 'worker' — ", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Model_growing_features_tell_the_team_to_add_a_migration()
    {
        using var app = new FakeApp();
        var output = new StringWriter();
        Assert.Equal(0, Add("softdelete", app, new FakeProcessRunner(), output: output));
        Assert.Contains("  → the model grew: run `goldpath db add AddSoftdelete` and commit the migration (production applies the bundle — migrations RFC D5)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Features_that_leave_the_model_alone_do_not_ask_for_a_migration()
    {
        using var app = new FakeApp();
        var output = new StringWriter();
        Assert.Equal(0, Add("dataprotection", app, new FakeProcessRunner(), output: output));
        Assert.DoesNotContain("goldpath db add", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("  → classify once: [GoldpathPersonalData] on sensitive properties — every sink (audit rows, logs) masks them", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Features_without_endpoints_never_look_for_the_endpoints_anchor()
    {
        using var app = new FakeApp();
        // A team that dropped the endpoints anchor must still be able to add a feature that maps none.
        File.WriteAllText(app.Program, app.Read(app.Program).Replace("// goldpath:features endpoints — admin surfaces map here (put them behind the auth floor)\n", string.Empty, StringComparison.Ordinal));
        Assert.Equal(0, Add("dataprotection", app, new FakeProcessRunner()));
        Assert.Contains("builder.AddGoldpathDataProtection();", app.Read(app.Program), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_failure_restores_the_already_written_files_and_fails_loud()
    {
        using var app = new FakeApp();
        // Manifest + csproj are written BEFORE Program.cs is edited; the missing endpoints anchor
        // blows up in between, so the rollback must undo what already landed.
        File.WriteAllText(app.Program, app.Read(app.Program).Replace("// goldpath:features endpoints — admin surfaces map here (put them behind the auth floor)\n", string.Empty, StringComparison.Ordinal));
        var before = new[] { app.Manifest, app.ApiProject, app.AppHostProject, app.Program, app.Model, app.AppHost }.ToDictionary(p => p, app.Read);
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(1, Add("archival", app, new FakeProcessRunner(), output, error));

        Assert.Contains("anchor '// goldpath:features endpoints' not found", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("added — engine clean", output.ToString(), StringComparison.Ordinal);
        foreach (var (path, content) in before)
        {
            Assert.Equal(content, app.Read(path));
        }
    }

    // ---- every recipe through the CLI: plan lines land verbatim, and a second run is a no-op ----

    public static TheoryData<string> AllFeatures()
    {
        var data = new TheoryData<string>();
        foreach (var name in FeatureRecipes.Names)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllFeatures))]
    public void Every_recipe_lands_its_plan_verbatim_and_is_idempotent(string feature)
    {
        using var app = new FakeApp(messagingWired: true);
        var plan = FeatureRecipes.Build(feature, AppFacts.Read(AppFiles.Locate(app.Root)));
        var runner = new FakeProcessRunner();
        var output = new StringWriter();

        Assert.Equal(0, Add(feature, app, runner, output));

        var program = app.Read(app.Program);
        var model = app.Read(app.Model);
        var appHost = app.Read(app.AppHost);
        var manifest = app.Read(app.Manifest);
        foreach (var package in plan.ApiPackages)
        {
            Assert.Contains($"    <PackageReference Include=\"{package}\" />", app.Read(app.ApiProject), StringComparison.Ordinal);
        }

        foreach (var package in plan.AppHostPackages)
        {
            Assert.Contains($"    <PackageReference Include=\"{package}\" />", app.Read(app.AppHostProject), StringComparison.Ordinal);
        }

        foreach (var line in plan.Registrations.Concat(plan.Middleware).Concat(plan.Endpoints).Concat(plan.BusLines))
        {
            Assert.Contains($"\n{line}\n", program, StringComparison.Ordinal);
        }

        foreach (var line in plan.ModelCalls)
        {
            Assert.Contains($"\n{line}\n", model, StringComparison.Ordinal);
        }

        foreach (var line in plan.Resources.Concat(plan.References))
        {
            Assert.Contains($"\n{line}\n", appHost, StringComparison.Ordinal);
        }

        foreach (var line in plan.ManifestLines)
        {
            Assert.Contains($"\n{line}\n", manifest, StringComparison.Ordinal);
        }

        foreach (var step in plan.NextSteps)
        {
            Assert.Contains($"  → {step}", output.ToString(), StringComparison.Ordinal);
        }

        // Second run: already enabled — nothing rewritten, no engine round-trip.
        var engineRuns = runner.Calls.Count;
        var second = new StringWriter();
        Assert.Equal(0, Add(feature, app, runner, second));
        Assert.Equal($"goldpath: '{feature}' is already enabled ({plan.ManifestKey}) — nothing to do.{Environment.NewLine}", second.ToString());
        Assert.Equal(engineRuns, runner.Calls.Count);
        Assert.Equal(program, app.Read(app.Program));
        Assert.Equal(model, app.Read(app.Model));
        Assert.Equal(appHost, app.Read(app.AppHost));
        Assert.Equal(manifest, app.Read(app.Manifest));
    }

    [Fact]
    public void Registration_blocks_keep_their_order_in_the_composition_root()
    {
        using var app = new FakeApp();
        Assert.Equal(0, Add("approvals", app, new FakeProcessRunner()));

        var lines = app.Read(app.Program).Split('\n');
        var anchor = Array.FindIndex(lines, l => l.Contains("goldpath:features registrations", StringComparison.Ordinal));
        Assert.Equal(
            [
                "builder.AddGoldpathJobs<WebApplicationBuilder, ShopDbContext>(jobs =>",
                "{",
                "    jobs.ConnectionName = \"shopdb\";              // runs + schedules live in the app database",
                "    jobs.AddGoldpathApprovalsJobs();               // escalation sweep — overdue rungs move up, the top rung expires",
                "});",
                "builder.AddGoldpathApprovals<WebApplicationBuilder, ShopDbContext>(approvals =>",
                "{",
                "    // Declare YOUR authority chains here (goldpath never guesses who may approve):",
                "    // approvals.AddLadder(\"credit-limit\", l => l",
                "    //     .Rung(\"expert\", 1_000_000m, TimeSpan.FromHours(8))",
                "    //     .Rung(\"manager\", 5_000_000m, TimeSpan.FromHours(8), requiredApprovals: 2)   // quorum is a rung property",
                "    //     .TopRung(\"general-manager\", TimeSpan.FromHours(24)));",
                "});",
            ],
            lines.Skip(anchor + 1).Take(13));
    }
}
