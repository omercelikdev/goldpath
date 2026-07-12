using Xunit;

namespace Goldpath.Cli.Tests;

public class AppFilesTests
{
    [Fact]
    public void Locate_resolves_every_anchored_file_by_content()
    {
        using var app = new FakeApp();
        var files = AppFiles.Locate(app.Root);

        Assert.Equal(app.ApiProject, files.ApiProject);
        Assert.Equal(app.AppHostProject, files.AppHostProject);
        Assert.Equal(app.Program, files.ProgramFile);
        Assert.Equal(app.Model, files.ModelFile);
        Assert.Equal(app.AppHost, files.AppHostFile);
        Assert.Equal(app.Manifest, files.ManifestFile);
    }

    [Fact]
    public void Files_under_bin_and_obj_are_invisible()
    {
        using var app = new FakeApp();
        var bin = Path.Combine(app.Root, "src/Shop.Api/bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "Copy.cs"), "// goldpath:features registrations — stale build output");

        Assert.Equal(app.Program, AppFiles.Locate(app.Root).ProgramFile);
    }

    [Fact]
    public void Two_files_with_the_same_anchor_fail_with_both_paths_named()
    {
        using var app = new FakeApp();
        var duplicate = Path.Combine(app.Root, "src/Shop.Api/Program2.cs");
        File.WriteAllText(duplicate, "// goldpath:features registrations — copied by accident");

        var exception = Assert.Throws<CliFailureException>(() => AppFiles.Locate(app.Root));
        Assert.Contains("2 files", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Program2.cs", exception.Message, StringComparison.Ordinal);
        Assert.Contains("keep exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_anchor_names_the_anchor_and_the_file_kind()
    {
        using var app = new FakeApp();
        File.WriteAllText(app.AppHost, "var builder = DistributedApplication.CreateBuilder(args);\n");

        var exception = Assert.Throws<CliFailureException>(() => AppFiles.Locate(app.Root));
        Assert.Contains("AppHost.cs", exception.Message, StringComparison.Ordinal);
        Assert.Contains("goldpath:features resources", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_manifest_teaches_the_path_flag()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"goldpath-cli-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            var exception = Assert.Throws<CliFailureException>(() => AppFiles.Locate(empty));
            Assert.Contains("--path", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }
}
