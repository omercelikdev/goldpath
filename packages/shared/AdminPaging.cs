namespace Goldpath;

/// <summary>
/// The admin surface's paging clamp (frozen contract: docs/rfc/goldpath-admin-contract.md).
/// Every list verb's caller-supplied <c>take</c> rides through here, so an absurd value can
/// never become an unbounded query. Compile-linked into each module (the AdminSurfaceGuard
/// dedupe pattern).
/// </summary>
internal static class AdminPaging
{
    /// <summary>One page's hard ceiling — larger reads paginate (keyset where offered).</summary>
    internal const int MaxTake = 500;

    /// <summary>Clamps to [1, MaxTake]: zero/negative asks still answer with one row, honestly.</summary>
    internal static int Clamp(int take) => Math.Clamp(take, 1, MaxTake);
}
