namespace Goldpath;

/// <summary>
/// Module options: the declared file rails. A rail is DATA plus baked, compile-checked
/// closures (fileexchange RFC D3) — a new counterparty is a new declaration, never new
/// engine code.
/// </summary>
public sealed class GoldpathFileExchangeOptions
{
    internal Dictionary<string, GoldpathFileRailDefinition> RailMap { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The declared rails by name.</summary>
    public IReadOnlyDictionary<string, GoldpathFileRailDefinition> Rails => RailMap;

    /// <summary>Declares one inbound file rail for rows of <typeparamref name="TRow"/>.</summary>
    public GoldpathFileExchangeOptions AddRail<TRow>(string name, Action<GoldpathFileRailBuilder<TRow>> configure)
    {
        var builder = new GoldpathFileRailBuilder<TRow>(name);
        configure(builder);
        RailMap[name] = builder.Build();
        return this;
    }
}

/// <summary>A declared rail with its execution baked as typed closures at registration time.</summary>
public sealed class GoldpathFileRailDefinition
{
    internal GoldpathFileRailDefinition(string name)
    {
        Name = name;
    }

    /// <summary>Rail name (the key a file names on arrival).</summary>
    public string Name { get; }

    /// <summary>Lines skipped before data starts (a declared header).</summary>
    public int HeaderLines { get; internal set; }

    internal Func<IReadOnlyList<string>, string?> ValidateFileCore { get; set; } = _ => null;
    internal Func<string, CancellationToken, Task<string?>> ProcessRowCore { get; set; } = null!;
}

/// <summary>Fluent shape for one rail.</summary>
public sealed class GoldpathFileRailBuilder<TRow>
{
    private readonly string _name;
    private int _headerLines;
    private Func<IReadOnlyList<string>, string?>? _validateFile;
    private Func<string, TRow>? _parse;
    private Func<TRow, string?>? _validateRow;
    private Func<TRow, CancellationToken, Task>? _handle;

    internal GoldpathFileRailBuilder(string name) => _name = name;

    /// <summary>Declares header lines to skip before data rows begin.</summary>
    public GoldpathFileRailBuilder<TRow> Header(int lines)
    {
        _headerLines = lines;
        return this;
    }

    /// <summary>File-level contract: return a reason to REJECT the whole file (truncation,
    /// trailer-count mismatch), or null to accept. A rejected file ingests nothing.</summary>
    public GoldpathFileRailBuilder<TRow> ValidateFile(Func<IReadOnlyList<string>, string?> validate)
    {
        _validateFile = validate;
        return this;
    }

    /// <summary>Parses one data line; a throw here quarantines the row.</summary>
    public GoldpathFileRailBuilder<TRow> ParseLine(Func<string, TRow> parse)
    {
        _parse = parse;
        return this;
    }

    /// <summary>Row-level contract: return a reason to QUARANTINE the row, or null.</summary>
    public GoldpathFileRailBuilder<TRow> ValidateRow(Func<TRow, string?> validate)
    {
        _validateRow = validate;
        return this;
    }

    /// <summary>The business handler for one accepted row; a throw here quarantines it.</summary>
    public GoldpathFileRailBuilder<TRow> Handle(Func<TRow, CancellationToken, Task> handle)
    {
        _handle = handle;
        return this;
    }

    internal GoldpathFileRailDefinition Build()
    {
        if (_parse is null || _handle is null)
        {
            throw new InvalidOperationException($"Rail '{_name}' needs ParseLine(...) and Handle(...) — the contract must be modeled, never guessed.");
        }

        var parse = _parse;
        var validateRow = _validateRow;
        var handle = _handle;

        var definition = new GoldpathFileRailDefinition(_name) { HeaderLines = _headerLines };
        if (_validateFile is not null)
        {
            definition.ValidateFileCore = _validateFile;
        }

        // Baked per-row pipeline: parse -> validate -> handle. Returns a quarantine
        // reason, or null when the row applied.
        definition.ProcessRowCore = async (line, ct) =>
        {
            TRow row;
            try
            {
                row = parse(line);
            }
            catch (Exception ex)
            {
                return $"parse: {ex.Message}";
            }

            if (validateRow?.Invoke(row) is { } reason)
            {
                return reason;
            }

            try
            {
                await handle(row, ct);
            }
            catch (Exception ex)
            {
                return $"handle: {ex.Message}";
            }

            return null;
        };

        return definition;
    }
}
