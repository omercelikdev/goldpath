using CsCheck;
using Xunit;

namespace Goldpath.Cli.Tests;

public class TextEditsTests
{
    [Fact]
    public void Insert_lands_immediately_after_the_anchor_line()
    {
        var text = "a\n// anchor here\nb";
        var result = TextEdits.InsertAfterAnchor(text, "anchor here", ["x", "y"]);
        Assert.Equal("a\n// anchor here\nx\ny\nb", result);
    }

    [Fact]
    public void Insert_is_idempotent_at_block_level()
    {
        var once = TextEdits.InsertAfterAnchor("// a\n", "// a", ["x", "y"]);
        var twice = TextEdits.InsertAfterAnchor(once, "// a", ["x", "y"]);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Structural_lines_that_repeat_in_the_file_never_break_a_block()
    {
        // Regression (caught by the e2e proof): the app already contains "{" and "});" from
        // other lambdas — a per-line dedup would silently drop the block's braces.
        var text = "// anchor\nexisting(x =>\n{\n    work();\n});";
        var block = new[] { "added(o =>", "{", "    o.Value = 1;", "});" };

        var result = TextEdits.InsertAfterAnchor(text, "// anchor", block);

        var lines = result.Split('\n');
        Assert.Equal(block, lines[1..5]);
    }

    [Fact]
    public void Missing_anchor_fails_with_the_anchor_name()
    {
        var exception = Assert.Throws<CliFailureException>(() => TextEdits.InsertAfterAnchor("a\nb", "nope", ["x"]));
        Assert.Contains("'nope'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveLinesContaining_removes_only_matching_lines()
    {
        Assert.Equal("a\nc", TextEdits.RemoveLinesContaining("a\nkill me\nc", "kill"));
    }

    [Fact]
    public void Insert_preserves_every_original_line_and_their_order()
    {
        // Property: for arbitrary documents, insertion never loses or reorders content.
        var line = Gen.String[Gen.Char.AlphaNumeric, 0, 8];
        var doc = line.List[1, 20];
        doc.Sample(lines =>
        {
            var text = string.Join('\n', ["// A", .. lines]);
            var result = TextEdits.InsertAfterAnchor(text, "// A", ["INSERTED-LINE"]).Split('\n');
            var survivors = result.Where(l => l != "INSERTED-LINE").ToArray();
            return survivors.SequenceEqual(text.Split('\n'));
        });
    }

    [Fact]
    public void Manifest_editor_appends_a_features_block_when_none_exists()
    {
        var result = ManifestEditor.AddFeatureLines("kind: solution\nname: X\n", ["  softDelete: true"]);
        Assert.Contains("features:\n  softDelete: true", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_editor_extends_an_existing_features_block()
    {
        var manifest = "kind: solution\nfeatures:\n  auditTrail: true\n";
        var result = ManifestEditor.AddFeatureLines(manifest, ["  softDelete: true"]);
        Assert.Contains("features:\n  softDelete: true\n  auditTrail: true", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_editor_detects_enabled_keys_only_at_feature_indent()
    {
        Assert.True(ManifestEditor.IsEnabled("features:\n  auditTrail: true\n", "auditTrail"));
        Assert.False(ManifestEditor.IsEnabled("features:\n  auditTrail: true\n", "softDelete"));
        Assert.False(ManifestEditor.IsEnabled("# auditTrail: commented\n", "auditTrail"));
    }

    [Fact]
    public void Manifest_editor_reads_the_kind()
    {
        Assert.Equal("worker", ManifestEditor.ReadKind("schemaVersion: 1\nkind: worker\n"));
        Assert.Null(ManifestEditor.ReadKind("name: x\n"));
    }
}
