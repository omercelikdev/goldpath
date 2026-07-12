using Xunit;

namespace Goldpath.Archival.Tests;

public class RedactorTests
{
    private sealed class Node
    {
        public string? Name { get; set; }              // classified (FakeProtector)
        public string? Note { get; set; }              // not classified
        public int Count { get; set; }
        public Node? Next { get; set; }                // cycle bait
        public List<Node> Children { get; set; } = [];
        public string ReadOnly => "Name-shaped but unwritable";
    }

    [Fact]
    public void Redacts_only_classified_writable_changed_strings_across_the_graph()
    {
        var protector = new FakeProtector();
        var root = new Node
        {
            Name = "gizli",
            Note = "açık",
            Children = [new Node { Name = "çocuk-gizli", Note = "çocuk-açık" }],
            Next = new Node { Name = null },   // null classified value: untouched, uncounted
        };
        root.Next.Next = root;                 // cycle — must terminate

        var redacted = GoldpathDocumentRedactor.Redact(root, protector);

        Assert.Equal(2, redacted);             // root.Name + child.Name; null skipped
        Assert.Equal("***", root.Name);
        Assert.Equal("açık", root.Note);
        Assert.Equal("***", root.Children[0].Name);
        Assert.Equal("çocuk-açık", root.Children[0].Note);
        Assert.Null(root.Next.Name);

        // Idempotence: a second pass changes nothing (erasure evidence stays honest).
        Assert.Equal(0, GoldpathDocumentRedactor.Redact(root, protector));
    }

    [Fact]
    public void Framework_types_and_primitives_are_never_walked()
    {
        var protector = new FakeProtector();
        Assert.Equal(0, GoldpathDocumentRedactor.Redact("just a string", protector));
        Assert.Equal(0, GoldpathDocumentRedactor.Redact(42, protector));
        Assert.Equal(0, GoldpathDocumentRedactor.Redact(new Uri("https://goldpath.local"), protector));
    }
}
