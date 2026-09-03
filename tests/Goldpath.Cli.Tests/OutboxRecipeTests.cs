using Xunit;

namespace Goldpath.Cli.Tests;

/// <summary>
/// The outbox recipe (the fourteenth — the ONE schema key the CLI could not compose): the
/// bus is born with the broker resource when absent, and joined when present; the model
/// gains the three MassTransit tables; the using the template hides behind a preprocessor
/// symbol is written for real. Plus the console riding the FIRST jobs feature.
/// </summary>
public class OutboxRecipeTests
{
    private static AppFacts Facts(bool messaging, string provider = "postgres", bool console = false) => new()
    {
        DbContextName = "ShopDbContext",
        DatabaseProvider = provider,
        ConnectionName = "shopdb",
        CachingWired = false,
        JobsWired = false,
        MessagingWired = messaging,
        AuthWired = true,
        ConsoleWired = console,
    };

    [Fact]
    public void Without_a_bus_the_outbox_recipe_births_one_with_the_broker_resource()
    {
        var plan = FeatureRecipes.Build("outbox", Facts(messaging: false));

        Assert.Equal("outbox", plan.ManifestKey);
        Assert.Equal(["Goldpath.Messaging", "MassTransit.RabbitMQ"], plan.ApiPackages);
        Assert.Equal(["Aspire.Hosting.RabbitMQ"], plan.AppHostPackages);
        Assert.Equal(["MassTransit"], plan.Usings);
        Assert.Equal(["var messaging = builder.AddRabbitMQ(\"messaging\");"], plan.Resources);
        Assert.Equal(["    .WithReference(messaging).WaitFor(messaging)"], plan.References);
        Assert.Equal("builder.AddGoldpathMessaging(bus =>", plan.Registrations[0]);
        Assert.Contains("    // goldpath:features consumers — bus-riding features register here", plan.Registrations);
        Assert.Contains("    bus.AddGoldpathOutbox<ShopDbContext>(outbox =>", plan.Registrations);
        Assert.Contains("        outbox.UsePostgres();", plan.Registrations);
        Assert.Contains("        cfg.ConfigureGoldpathEndpoints(context);", plan.Registrations);
        Assert.Empty(plan.BusLines);
        Assert.Equal(
            [
                "        // Transactional outbox/inbox tables (features.outbox in the manifest).",
                "        modelBuilder.AddInboxStateEntity();",
                "        modelBuilder.AddOutboxMessageEntity();",
                "        modelBuilder.AddOutboxStateEntity();",
            ],
            plan.ModelCalls);
        Assert.Equal(["  outbox: true"], plan.ManifestLines);
        Assert.Equal(3, plan.NextSteps.Count);
    }

    [Fact]
    public void With_a_bus_the_outbox_joins_it_and_brings_no_broker()
    {
        var plan = FeatureRecipes.Build("outbox", Facts(messaging: true, provider: "sqlserver"));

        Assert.Equal(["Goldpath.Messaging"], plan.ApiPackages);
        Assert.Empty(plan.AppHostPackages);
        Assert.Empty(plan.Resources);
        Assert.Empty(plan.Registrations);
        Assert.Equal(
            [
                "    bus.AddGoldpathOutbox<ShopDbContext>(outbox =>",
                "    {",
                "        outbox.UseSqlServer();",
                "    });",
            ],
            plan.BusLines);
    }

    [Fact]
    public void No_database_provider_means_no_outbox_said_plainly()
    {
        var refusal = Assert.Throws<CliFailureException>(() => FeatureRecipes.Build("outbox", Facts(messaging: false, provider: "none")));
        Assert.Contains("outbox tables live in the app database", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_outbox_lands_end_to_end_using_included()
    {
        using var app = new FakeApp();   // no messaging wired
        var runner = new FakeProcessRunner();

        Assert.Equal(0, CliRunner.Run(["add", "feature", "outbox", "--path", app.Root], runner, TextWriter.Null, TextWriter.Null));

        var program = app.Read(app.Program);
        Assert.StartsWith("using MassTransit;", program, StringComparison.Ordinal);
        Assert.Contains("builder.AddGoldpathMessaging(bus =>", program, StringComparison.Ordinal);
        Assert.Contains("bus.AddGoldpathOutbox<ShopDbContext>(outbox =>", program, StringComparison.Ordinal);
        Assert.Contains("modelBuilder.AddOutboxMessageEntity();", app.Read(app.Model), StringComparison.Ordinal);
        Assert.Contains("var messaging = builder.AddRabbitMQ(\"messaging\");", app.Read(app.AppHost), StringComparison.Ordinal);
        Assert.Contains(".WithReference(messaging).WaitFor(messaging)", app.Read(app.AppHost), StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Aspire.Hosting.RabbitMQ\" />", app.Read(app.AppHostProject), StringComparison.Ordinal);
        Assert.Contains("  outbox: true", app.Read(app.Manifest), StringComparison.Ordinal);

        // Idempotent: a second run writes nothing twice — one using, one bus.
        Assert.Equal(0, CliRunner.Run(["add", "feature", "outbox", "--path", app.Root], runner, TextWriter.Null, TextWriter.Null));
        var again = app.Read(app.Program);
        Assert.Equal(1, again.Split("using MassTransit;").Length - 1);
        Assert.Equal(1, again.Split("builder.AddGoldpathMessaging(bus =>").Length - 1);
    }

    [Fact]
    public void The_first_jobs_rider_brings_the_console_the_next_one_finds_it()
    {
        var first = FeatureRecipes.Build("archival", Facts(messaging: true, console: false));
        Assert.Contains("Goldpath.Console", first.ApiPackages);
        Assert.Equal("app.MapGoldpathConsole();                           // behind the SAME ops floor as the surfaces", first.Endpoints[^1]);

        var next = FeatureRecipes.Build("bulk", Facts(messaging: true, console: true));
        Assert.DoesNotContain("Goldpath.Console", next.ApiPackages);
        Assert.DoesNotContain(next.Endpoints, line => line.Contains("MapGoldpathConsole", StringComparison.Ordinal));

        // No auth strategy: the opt-out is WRITTEN, exactly as the template does it.
        var open = FeatureRecipes.Build("bulk", new AppFacts
        {
            DbContextName = "ShopDbContext",
            DatabaseProvider = "postgres",
            ConnectionName = "shopdb",
            CachingWired = false,
            JobsWired = false,
            MessagingWired = true,
            AuthWired = false,
            ConsoleWired = false,
        });
        Assert.StartsWith("app.MapGoldpathConsole(exposeUnsecured: true);", open.Endpoints[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_ring_b_feature_never_drags_the_console_in()
    {
        var plan = FeatureRecipes.Build("audittrail", Facts(messaging: true));
        Assert.DoesNotContain("Goldpath.Console", plan.ApiPackages);
        Assert.Empty(plan.Endpoints);
    }
}
