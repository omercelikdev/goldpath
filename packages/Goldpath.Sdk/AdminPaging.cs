namespace Goldpath;

/// <summary>
/// The admin surface's paging clamp (frozen contract: docs/rfc/goldpath-admin-contract.md).
/// Every list verb's caller-supplied <c>take</c> rides through here, so an absurd value can
/// never become an unbounded query. Shipped in Goldpath.Sdk — one seam for every module (platform RFC D1).
/// </summary>
public static class AdminPaging
{
    /// <summary>One page's hard ceiling — larger reads paginate (keyset where offered).</summary>
    public const int MaxTake = 500;

    /// <summary>Clamps to [1, MaxTake]: zero/negative asks still answer with one row, honestly.</summary>
    public static int Clamp(int take) => Math.Clamp(take, 1, MaxTake);
}
