namespace Goldpath.Cli;

/// <summary>
/// <c>goldpath discover</c> — walks a tree and lists every Goldpath manifest under it, with what
/// each one DECLARES (kind, name, products). The unit Goldpath binds to is the MANIFEST, never the
/// repo (foundation §5), so this is how a monorepo, a workspace of product repos, or a laptop full
/// of clones is inventoried without anyone keeping a list by hand — the enforcing half of the
/// platform RFC's R-6 antidote (visibility loss across separate repos).
///
/// It reads, and only reads: no engine call, no network, no write. Line-oriented output so a shell
/// can pipe it (<c>goldpath discover | awk …</c>) and a CI matrix can be built from it.
/// </summary>
public static class DiscoverCommand
{
    /// <summary>Directories that never hold a manifest and cost real time to walk.</summary>
    private static readonly string[] Skipped = ["node_modules", "bin", "obj", ".git", ".vs", "dist", "coverage", "TestResults"];

    /// <summary>Walks <paramref name="root"/> and prints one line per manifest found.</summary>
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var start = string.IsNullOrWhiteSpace(root) ? Directory.GetCurrentDirectory() : Path.GetFullPath(root);
        if (!Directory.Exists(start))
        {
            throw new CliUsageException($"no such directory: {start}");
        }

        var manifests = Find(start).OrderBy(path => path, StringComparer.Ordinal).ToList();
        if (manifests.Count == 0)
        {
            // An empty inventory is an ANSWER, not a failure: it is how "nothing declares itself
            // here" is reported to a human and to a CI matrix alike.
            output.WriteLine($"── no Goldpath manifests under {start}");
            return 0;
        }

        foreach (var manifest in manifests)
        {
            var text = ReadOrEmpty(manifest, error);
            var solutionRoot = Path.GetDirectoryName(Path.GetDirectoryName(manifest)) ?? start;
            var relative = Path.GetRelativePath(start, solutionRoot);
            var products = Products(text);
            output.WriteLine(
                $"{(relative == "." ? "." : relative)}  kind={Scalar(text, "kind") ?? "?"}  name={Scalar(text, "name") ?? "?"}" +
                (products.Count > 0 ? $"  products={string.Join(",", products)}" : ""));
        }

        output.WriteLine($"── {manifests.Count} manifest(s) under {start}");
        return 0;
    }

    /// <summary>Every <c>.goldpath/manifest.yaml</c> under the root, skipping build/vendor trees.</summary>
    private static IEnumerable<string> Find(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var manifest = Path.Combine(directory, ".goldpath", "manifest.yaml");
            if (File.Exists(manifest))
            {
                yield return manifest;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;   // a directory we may not read is not an inventory failure
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (!Skipped.Contains(name, StringComparer.Ordinal))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static string ReadOrEmpty(string path, TextWriter error)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            error.WriteLine($"goldpath: could not read {path} — {exception.Message}");
            return "";
        }
    }

    /// <summary>
    /// A top-level scalar (<c>kind: solution</c>). Deliberately string surgery, not a YAML parser:
    /// discover must never disagree with the ENGINE about what a manifest means — the engine
    /// (specdrift) validates, this only reports what a reader would see.
    /// </summary>
    private static string? Scalar(string manifest, string key)
    {
        foreach (var line in manifest.Split('\n'))
        {
            if (line.StartsWith($"{key}:", StringComparison.Ordinal))
            {
                var value = line[(key.Length + 1)..].Trim();
                return value.Length == 0 ? null : value.Trim('"', '\'');
            }
        }

        return null;
    }

    /// <summary>The namespaced product names a manifest declares (ADR-0012's `products` array).</summary>
    private static List<string> Products(string manifest)
    {
        var names = new List<string>();
        var inProducts = false;
        foreach (var raw in manifest.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("products:", StringComparison.Ordinal))
            {
                inProducts = true;
                continue;
            }

            if (inProducts)
            {
                // The array ends at the next top-level key (a non-indented, non-empty line).
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('-'))
                {
                    break;
                }

                var trimmed = line.Trim();
                if (trimmed.StartsWith("- name:", StringComparison.Ordinal))
                {
                    names.Add(trimmed["- name:".Length..].Trim().Trim('"', '\''));
                }
                else if (trimmed.StartsWith("name:", StringComparison.Ordinal))
                {
                    names.Add(trimmed["name:".Length..].Trim().Trim('"', '\''));
                }
            }
        }

        return names;
    }
}
