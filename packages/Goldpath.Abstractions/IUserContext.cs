namespace Goldpath;

/// <summary>
/// Ambient user of the current execution flow (HTTP claims, message headers, job identity).
/// Consumers must treat <see langword="null"/> as "system" (background work, startup).
/// Audit without "who" is not audit — this is the contract that carries the who.
/// </summary>
public interface IUserContext
{
    /// <summary>The current user identifier, or <see langword="null"/> for system flows.</summary>
    string? UserId { get; }
}
