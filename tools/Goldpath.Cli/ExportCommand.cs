using System.Text;
using System.Text.RegularExpressions;

namespace Goldpath.Cli;

/// <summary>
/// <c>goldpath export compose</c> — the container story's first tier past local
/// (foundation §10, platform RFC D6): a docker-compose.yml GENERATED from the AppHost
/// definition, never hand-written, so the two can never diverge. The transform is
/// textual over the GENERATED app's AppHost.cs (the D4 boundary — anchors and documented
/// vocabulary, no Roslyn), and re-running regenerates: compose is an ARTIFACT of the
/// AppHost, edits belong upstream. Dockerfiles are laid per project only when absent.
/// </summary>
public static class ExportCommand
{
    /// <summary>One AppHost resource (container or project) with its wiring.</summary>
    public sealed record Resource(
        string Variable,
        string Kind,
        string Name,
        string? DatabaseName,
        string? ProjectSafe,
        List<string> References,
        List<string> WaitsFor,
        Dictionary<string, string> Environment,
        bool HasHealthCheck);

    /// <summary>Runs the export against the app root.</summary>
    public static int Run(string appRoot, TextWriter output, TextWriter error)
    {
        var files = AppFiles.Locate(appRoot);
        var resources = Parse(File.ReadAllText(files.AppHostFile));
        if (resources.Count == 0)
        {
            throw new CliFailureException("the AppHost declares no resources — nothing to export.");
        }

        var compose = WriteCompose(resources, safe => FindProjectDir(appRoot, safe) is { } dir ? Path.GetFileName(dir) : null);
        File.WriteAllText(Path.Combine(appRoot, "docker-compose.yml"), compose);
        output.WriteLine("── docker-compose.yml generated FROM the AppHost (re-run after AppHost changes; edits belong upstream)");

        foreach (var project in resources.Where(r => r.Kind == "project"))
        {
            var projectDir = FindProjectDir(appRoot, project.ProjectSafe!);
            if (projectDir is null)
            {
                throw new CliFailureException($"no project directory matches Projects.{project.ProjectSafe} — the AppHost and src/ disagree.");
            }

            var dockerfile = Path.Combine(projectDir, "Dockerfile");
            if (!File.Exists(dockerfile))
            {
                File.WriteAllText(dockerfile, Dockerfile(Path.GetFileName(projectDir)));
                output.WriteLine($"── Dockerfile laid: {Path.GetRelativePath(appRoot, dockerfile)}");
            }
        }

        output.WriteLine("── compose is the DEV tier (fixed credentials, one node): environments stay CI-built manifests (foundation §10)");
        return 0;
    }

    /// <summary>
    /// Parses the GENERATED AppHost vocabulary: Add{Postgres|SqlServer|RabbitMQ|Redis},
    /// .AddDatabase, AddProject, .WithReference, .WaitFor, .WithEnvironment,
    /// .WithHttpHealthCheck. Chains may or may not name a variable.
    /// </summary>
    public static List<Resource> Parse(string appHost)
    {
        var resources = new List<Resource>();
        // Split into statements on ';' so each builder chain reads as one unit.
        foreach (var raw in appHost.Split(';'))
        {
            var statement = raw.Trim();
            var head = Regex.Match(statement,
                @"(?:var\s+(?<var>\w+)\s*=\s*)?builder\.Add(?<kind>Postgres|SqlServer|RabbitMQ|Redis|Project)(?:<Projects\.(?<safe>\w+)>)?\(""(?<name>[a-z0-9-]+)""\)");
            if (!head.Success)
            {
                continue;
            }

            var database = Regex.Match(statement, @"\.AddDatabase\(""(?<db>[a-z0-9-]+)""\)");
            var resource = new Resource(
                head.Groups["var"].Success ? head.Groups["var"].Value : head.Groups["name"].Value,
                head.Groups["kind"].Value == "Project" ? "project" : head.Groups["kind"].Value.ToLowerInvariant(),
                head.Groups["name"].Value,
                database.Success ? database.Groups["db"].Value : null,
                head.Groups["safe"].Success ? head.Groups["safe"].Value : null,
                [.. Regex.Matches(statement, @"\.WithReference\((\w+)\)").Select(m => m.Groups[1].Value)],
                [.. Regex.Matches(statement, @"\.WaitFor\((\w+)\)").Select(m => m.Groups[1].Value)],
                Regex.Matches(statement, @"\.WithEnvironment\(""([^""]+)"",\s*""([^""]+)""\)")
                    .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal),
                statement.Contains(".WithHttpHealthCheck(", StringComparison.Ordinal));
            resources.Add(resource);
        }

        return resources;
    }

    private const string DevPassword = "goldpath-dev";

    /// <summary>Renders the compose file — hand-rolled YAML, deterministic order.</summary>
    public static string WriteCompose(List<Resource> resources, Func<string, string?> projectDirectory)
    {
        var byVariable = resources.ToDictionary(r => r.Variable, StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("# GENERATED by `goldpath export compose` FROM the AppHost — do not hand-edit;");
        sb.AppendLine("# change the AppHost and re-run (foundation §10: the two definitions cannot diverge).");
        sb.AppendLine("# DEV tier: fixed credentials, one node. Environments stay CI-built manifests.");
        sb.AppendLine("services:");

        foreach (var resource in resources)
        {
            sb.AppendLine($"  {resource.Name}:");
            switch (resource.Kind)
            {
                case "postgres":
                    sb.AppendLine("    image: postgres:17-alpine");
                    sb.AppendLine("    environment:");
                    sb.AppendLine($"      POSTGRES_PASSWORD: {DevPassword}");
                    if (resource.DatabaseName is not null)
                    {
                        sb.AppendLine($"      POSTGRES_DB: {resource.DatabaseName}");
                    }

                    sb.AppendLine("    healthcheck:");
                    sb.AppendLine("      test: [\"CMD-SHELL\", \"pg_isready -h 127.0.0.1 -U postgres\"]");
                    sb.AppendLine("      interval: 2s");
                    sb.AppendLine("      retries: 30");
                    break;
                case "sqlserver":
                    sb.AppendLine("    image: mcr.microsoft.com/mssql/server:2022-latest");
                    sb.AppendLine("    environment:");
                    sb.AppendLine("      ACCEPT_EULA: \"Y\"");
                    sb.AppendLine($"      MSSQL_SA_PASSWORD: {DevPassword}1!");
                    break;
                case "rabbitmq":
                    sb.AppendLine("    image: rabbitmq:4");
                    sb.AppendLine("    healthcheck:");
                    sb.AppendLine("      test: [\"CMD\", \"rabbitmq-diagnostics\", \"-q\", \"check_port_connectivity\"]");
                    sb.AppendLine("      interval: 3s");
                    sb.AppendLine("      retries: 30");
                    break;
                case "redis":
                    sb.AppendLine("    image: redis:7-alpine");
                    break;
                case "project":
                    WriteProject(sb, resource, byVariable, projectDirectory);
                    break;
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void WriteProject(StringBuilder sb, Resource project, Dictionary<string, Resource> byVariable, Func<string, string?> projectDirectory)
    {
        var directory = projectDirectory(project.ProjectSafe!)
            ?? throw new CliFailureException($"no project directory matches Projects.{project.ProjectSafe} — the AppHost and src/ disagree.");
        sb.AppendLine("    build:");
        sb.AppendLine("      context: .");
        sb.AppendLine($"      dockerfile: src/{directory}/Dockerfile");
        sb.AppendLine("    ports:");
        sb.AppendLine("      - \"8080\"   # random host port — `docker compose port {0} 8080`".Replace("{0}", project.Name));
        sb.AppendLine("    environment:");
        sb.AppendLine("      ASPNETCORE_URLS: http://+:8080");
        sb.AppendLine("      # Dev tier: migrations apply on boot; ENVIRONMENTS run the CI bundle (migrations D4).");
        sb.AppendLine("      ASPNETCORE_ENVIRONMENT: Development");
        foreach (var (key, value) in project.Environment)
        {
            sb.AppendLine($"      {key.Replace(":", "__", StringComparison.Ordinal)}: \"{value}\"");
        }

        foreach (var reference in project.References)
        {
            if (!byVariable.TryGetValue(reference, out var target))
            {
                continue;
            }

            switch (target.Kind)
            {
                case "postgres":
                    sb.AppendLine($"      ConnectionStrings__{target.DatabaseName}: Host={target.Name};Port=5432;Database={target.DatabaseName};Username=postgres;Password={DevPassword}");
                    break;
                case "sqlserver":
                    sb.AppendLine($"      ConnectionStrings__{target.DatabaseName}: Server={target.Name},1433;Database={target.DatabaseName};User Id=sa;Password={DevPassword}1!;TrustServerCertificate=true");
                    break;
                case "rabbitmq":
                    sb.AppendLine($"      ConnectionStrings__{target.Name}: amqp://guest:guest@{target.Name}:5672");
                    break;
                case "redis":
                    sb.AppendLine($"      ConnectionStrings__{target.Name}: {target.Name}:6379");
                    break;
                case "project":
                    // Aspire service discovery's config shape — the gateway resolves heads by it.
                    sb.AppendLine($"      services__{target.Name}__http__0: http://{target.Name}:8080");
                    break;
            }
        }

        var dependencies = project.WaitsFor
            .Concat(project.References.Where(r => byVariable.TryGetValue(r, out var t) && t.Kind == "project"))
            .Select(v => byVariable.TryGetValue(v, out var t) ? t : null)
            .Where(t => t is not null)
            .Select(t => t!)
            .DistinctBy(t => t.Name)
            .ToList();
        if (dependencies.Count > 0)
        {
            sb.AppendLine("    depends_on:");
            foreach (var dependency in dependencies)
            {
                var condition = dependency.Kind is "postgres" or "rabbitmq" ? "service_healthy" : "service_started";
                sb.AppendLine($"      {dependency.Name}:");
                sb.AppendLine($"        condition: {condition}");
            }
        }
    }

    private static string? FindProjectDir(string appRoot, string safe)
        => Directory.GetDirectories(Path.Combine(appRoot, "src"))
            .FirstOrDefault(dir => Path.GetFileName(dir).Replace('.', '_').Replace('-', '_') == safe);

    private static string Dockerfile(string projectName) => $"""
# GENERATED by `goldpath export compose` (laid once — edit freely afterwards).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY . .
# The host's global.json pins an exact SDK the image may not carry — inside the container
# the IMAGE TAG is the determinism, so the pin steps aside for the build.
RUN rm -f global.json && dotnet publish src/{projectName}/{projectName}.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "{projectName}.dll"]
""";
}
