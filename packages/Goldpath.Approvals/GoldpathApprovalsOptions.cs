namespace Goldpath;

/// <summary>
/// Module options: the declared approval ladders. Ladders are DATA (approvals RFC D2) —
/// a new authority chain is a new declaration, never a fork of the engine.
/// </summary>
public sealed class GoldpathApprovalsOptions
{
    internal Dictionary<string, GoldpathApprovalLadder> LadderMap { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long a delegation may last at most (cycle guard's time bound).</summary>
    public TimeSpan MaxDelegationWindow { get; set; } = TimeSpan.FromDays(14);

    /// <summary>The declared ladders by name.</summary>
    public IReadOnlyDictionary<string, GoldpathApprovalLadder> Ladders => LadderMap;

    /// <summary>Declares one amount-laddered authority chain.</summary>
    public GoldpathApprovalsOptions AddLadder(string name, Action<GoldpathApprovalLadderBuilder> configure)
    {
        var builder = new GoldpathApprovalLadderBuilder(name);
        configure(builder);
        LadderMap[name] = builder.Build();
        return this;
    }
}

/// <summary>A declared ladder: ordered rungs, lowest authority first.</summary>
public sealed class GoldpathApprovalLadder
{
    internal GoldpathApprovalLadder(string name, IReadOnlyList<GoldpathApprovalRung> rungs)
    {
        Name = name;
        Rungs = rungs;
    }

    /// <summary>Ladder name (the key an approval request names).</summary>
    public string Name { get; }

    /// <summary>The rungs, lowest authority first; the last rung is unbounded.</summary>
    public IReadOnlyList<GoldpathApprovalRung> Rungs { get; }

    /// <summary>The rung an amount routes to: the first whose ceiling covers it.</summary>
    public GoldpathApprovalRung Route(decimal amount)
    {
        foreach (var rung in Rungs)
        {
            if (rung.UpToInclusive is null || amount <= rung.UpToInclusive)
            {
                return rung;
            }
        }

        return Rungs[^1];
    }

    /// <summary>The rung above <paramref name="rung"/>, or null at the top.</summary>
    public GoldpathApprovalRung? Above(GoldpathApprovalRung rung)
    {
        var index = -1;
        for (var i = 0; i < Rungs.Count; i++)
        {
            if (Rungs[i].Role == rung.Role)
            {
                index = i;
                break;
            }
        }

        return index >= 0 && index + 1 < Rungs.Count ? Rungs[index + 1] : null;
    }
}

/// <summary>One authority rung: a role, its amount ceiling, and its decision deadline.</summary>
public sealed record GoldpathApprovalRung(string Role, decimal? UpToInclusive, TimeSpan EscalateAfter);

/// <summary>Fluent shape for one ladder.</summary>
public sealed class GoldpathApprovalLadderBuilder
{
    private readonly string _name;
    private readonly List<GoldpathApprovalRung> _rungs = [];

    internal GoldpathApprovalLadderBuilder(string name) => _name = name;

    /// <summary>Adds a rung with an inclusive amount ceiling and its decision deadline.</summary>
    public GoldpathApprovalLadderBuilder Rung(string role, decimal upToInclusive, TimeSpan escalateAfter)
    {
        _rungs.Add(new GoldpathApprovalRung(role, upToInclusive, escalateAfter));
        return this;
    }

    /// <summary>Adds the top rung — unbounded ceiling; overdue at the top EXPIRES.</summary>
    public GoldpathApprovalLadderBuilder TopRung(string role, TimeSpan escalateAfter)
    {
        _rungs.Add(new GoldpathApprovalRung(role, null, escalateAfter));
        return this;
    }

    internal GoldpathApprovalLadder Build()
    {
        if (_rungs.Count == 0)
        {
            throw new InvalidOperationException($"Ladder '{_name}' declares no rungs — an authority chain must be modeled, never guessed.");
        }

        if (_rungs[^1].UpToInclusive is not null)
        {
            throw new InvalidOperationException($"Ladder '{_name}' has no TopRung — every amount must route somewhere (declare the unbounded rung).");
        }

        decimal previous = 0;
        foreach (var ceiling in _rungs.Select(r => r.UpToInclusive).OfType<decimal>())
        {
            if (ceiling <= previous)
            {
                throw new InvalidOperationException($"Ladder '{_name}' ceilings must strictly increase.");
            }

            previous = ceiling;
        }

        return new GoldpathApprovalLadder(_name, _rungs.ToList());
    }
}
