using Xunit;

namespace Goldpath.Cli.Tests;

/// <summary>
/// Exact-behaviour tests for <c>goldpath init</c>: the prompts it asks, the manifest it
/// writes, the engine call it makes and the messages it refuses with — each one is the
/// user-facing contract, so every assertion is on the full text.
/// </summary>
public class InitCommandTests
{
    /// <summary>Records every question and answers by question prefix (default: empty).</summary>
    private sealed class ScriptedPrompter(Dictionary<string, string>? answers = null) : IPrompter
    {
        public List<string> Questions { get; } = [];

        public string Choose(string question, IReadOnlyList<string> choices, string defaultChoice) => defaultChoice;

        public IReadOnlyList<string> ChooseMany(string question, IReadOnlyList<string> choices) => [];

        public bool Confirm(string question, bool defaultAnswer) => defaultAnswer;

        public string Input(string question)
        {
            Questions.Add(question);
            return answers?.FirstOrDefault(a => question.StartsWith(a.Key, StringComparison.Ordinal)).Value ?? string.Empty;
        }
    }

    /// <summary>A disposable solution directory named <c>legacy-shop</c> (Pascal: LegacyShop).</summary>
    private sealed class Solution : IDisposable
    {
        private readonly string parent = Path.Combine(Path.GetTempPath(), $"goldpath-init-{Guid.NewGuid():N}");

        public Solution(bool sln = true, bool csproj = false)
        {
            Directory.CreateDirectory(Root);
            if (sln)
            {
                File.WriteAllText(Path.Combine(Root, "Legacy.sln"), "");
            }

            if (csproj)
            {
                Directory.CreateDirectory(Path.Combine(Root, "src", "Legacy.Api"));
                File.WriteAllText(Path.Combine(Root, "src", "Legacy.Api", "Legacy.Api.csproj"), "<Project />");
            }
        }

        public string Root => Path.Combine(parent, "legacy-shop");

        public string Manifest => Path.Combine(Root, ".goldpath", "manifest.yaml");

        public void Dispose() => Directory.Delete(parent, recursive: true);
    }

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string SchemaPath => Path.Combine(Path.GetTempPath(), "goldpath-manifest.schema.v1.json");

    [Fact]
    public void Init_asks_three_questions_with_the_directory_name_as_the_default()
    {
        using var solution = new Solution();
        var prompter = new ScriptedPrompter();

        Assert.Equal(0, InitCommand.Run(solution.Root, prompter, new FakeProcessRunner(), TextWriter.Null, TextWriter.Null));

        Assert.Equal(
            ["Solution name (default: LegacyShop)", "Owning team (kebab-case, e.g. team-orders)", "One-line description"],
            prompter.Questions);
    }

    [Fact]
    public void Blank_answers_fall_back_to_the_defaults_in_manifest_and_output()
    {
        using var solution = new Solution();
        // Whitespace is "no answer" — the fallback wins, not a blank field.
        var prompter = new ScriptedPrompter(new Dictionary<string, string> { ["Solution"] = "   ", ["Owning"] = "\t", ["One-line"] = "" });
        var output = new StringWriter();

        Assert.Equal(0, InitCommand.Run(solution.Root, prompter, new FakeProcessRunner(), output, TextWriter.Null));

        Assert.Equal("""
            # Attached by `goldpath init` (L2): the manifest is the single source of truth from here
            # on — grow it as capabilities adopt the golden path; `goldpath check` validates it.
            # Code rewiring stays YOURS until the transformation pack: init attaches, never rewrites.
            schemaVersion: 1
            kind: solution
            name: LegacyShop
            description: LegacyShop — attached to the golden path (L2)
            owner: platform-team
            """.Replace("\r\n", "\n", StringComparison.Ordinal), Lf(File.ReadAllText(solution.Manifest)));

        Assert.Equal(
            "── goldpath init: LegacyShop attached (kind: solution, owner: platform-team)\n"
            + "   the manifest is now this solution's single source of truth (ADR-0001);\n"
            + "   next: declare providers/features AS they adopt the path, `goldpath check` on every change;\n"
            + "   code rewiring and the skills family arrive with the transformation pack — init attaches, never rewrites.\n",
            Lf(output.ToString()));
    }

    [Fact]
    public void Answers_are_trimmed_and_land_verbatim_in_the_manifest()
    {
        using var solution = new Solution();
        var prompter = new ScriptedPrompter(new Dictionary<string, string>
        {
            ["Solution"] = "  Orders ",
            ["Owning"] = "team-orders\n",
            ["One-line"] = " Order intake ",
        });
        var output = new StringWriter();

        Assert.Equal(0, InitCommand.Run(solution.Root, prompter, new FakeProcessRunner(), output, TextWriter.Null));

        var manifest = Lf(File.ReadAllText(solution.Manifest));
        Assert.EndsWith("name: Orders\ndescription: Order intake\nowner: team-orders", manifest, StringComparison.Ordinal);
        Assert.StartsWith("── goldpath init: Orders attached (kind: solution, owner: team-orders)\n", Lf(output.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public void Init_validates_through_the_engine_with_exact_arguments()
    {
        using var solution = new Solution();
        var runner = new FakeProcessRunner();

        Assert.Equal(0, InitCommand.Run(solution.Root, new ScriptedPrompter(), runner, TextWriter.Null, TextWriter.Null));

        var call = Assert.Single(runner.Calls);
        Assert.Equal("specdrift", call.FileName);
        Assert.Equal(solution.Root, call.WorkingDirectory);
        // No .specdrift/rules.yaml in a fresh attach — schema only.
        Assert.Equal(["validate", Path.Combine(".goldpath", "manifest.yaml"), "--schema", SchemaPath], call.Arguments);
    }

    [Fact]
    public void A_rejected_manifest_fails_with_the_engine_message_and_leaves_nothing()
    {
        using var solution = new Solution();
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["validate"] = 3;
        var output = new StringWriter();

        var exception = Assert.Throws<CliFailureException>(() => InitCommand.Run(solution.Root, new ScriptedPrompter(), runner, output, TextWriter.Null));

        Assert.Equal("the engine rejected the manifest — nothing attached (fix the inputs and retry).", exception.Message);
        Assert.Equal(string.Empty, output.ToString());
        Assert.False(Directory.Exists(Path.Combine(solution.Root, ".goldpath")));
    }

    [Fact]
    public void A_rejected_manifest_keeps_a_goldpath_directory_that_held_other_files()
    {
        using var solution = new Solution();
        Directory.CreateDirectory(Path.Combine(solution.Root, ".goldpath"));
        File.WriteAllText(Path.Combine(solution.Root, ".goldpath", "notes.md"), "mine");
        var runner = new FakeProcessRunner();
        runner.ExitCodeWhenArgumentsContain["validate"] = 1;

        Assert.Throws<CliFailureException>(() => InitCommand.Run(solution.Root, new ScriptedPrompter(), runner, TextWriter.Null, TextWriter.Null));

        Assert.False(File.Exists(solution.Manifest));
        Assert.True(File.Exists(Path.Combine(solution.Root, ".goldpath", "notes.md")));
    }

    [Fact]
    public void An_attached_solution_is_refused_by_its_manifest_path_before_any_prompt()
    {
        using var solution = new Solution();
        Directory.CreateDirectory(Path.Combine(solution.Root, ".goldpath"));
        File.WriteAllText(solution.Manifest, "schemaVersion: 1\n");
        var prompter = new ScriptedPrompter();
        var runner = new FakeProcessRunner();

        var exception = Assert.Throws<CliFailureException>(() => InitCommand.Run(solution.Root, prompter, runner, TextWriter.Null, TextWriter.Null));

        Assert.Equal($"{solution.Manifest} already exists — this solution is attached; `goldpath check` is the daily verb.", exception.Message);
        Assert.Empty(prompter.Questions);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void A_directory_without_sln_or_csproj_is_refused_through_the_cli()
    {
        using var solution = new Solution(sln: false);
        var error = new StringWriter();

        // The CLI path: the refusal happens before the console prompter asks anything.
        Assert.Equal(1, CliRunner.Run(["init", "--path", solution.Root], new FakeProcessRunner(), TextWriter.Null, error));

        Assert.Equal($"goldpath: no .sln or .csproj under {solution.Root} — goldpath init attaches to an EXISTING solution (a new one starts with goldpath new).\n", Lf(error.ToString()));
        Assert.False(Directory.Exists(Path.Combine(solution.Root, ".goldpath")));
    }

    [Fact]
    public void A_nested_csproj_alone_is_enough_to_attach()
    {
        using var solution = new Solution(sln: false, csproj: true);

        Assert.Equal(0, InitCommand.Run(solution.Root, new ScriptedPrompter(), new FakeProcessRunner(), TextWriter.Null, TextWriter.Null));

        Assert.True(File.Exists(solution.Manifest));
    }

    [Fact]
    public void A_sln_alone_is_enough_to_attach()
    {
        using var solution = new Solution(sln: true, csproj: false);

        Assert.Equal(0, InitCommand.Run(solution.Root, new ScriptedPrompter(), new FakeProcessRunner(), TextWriter.Null, TextWriter.Null));

        Assert.True(File.Exists(solution.Manifest));
    }
}
