namespace Goldpath;

/// <summary>
/// One released item on the wire (the pacer publishes, competing consumers receive).
/// Carries COORDINATES only — the target payload stays in the durable item row; a broker
/// message is delivery plumbing, not a second source of truth.
/// </summary>
public sealed record GoldpathCampaignItemMessage(Guid CampaignId, long Seq, string Type) : IIntegrationEvent;

/// <summary>One item's outcome (consumers publish; the batching SINK flushes durable truth).</summary>
public sealed record GoldpathCampaignOutcomeMessage(Guid CampaignId, long Seq, bool Succeeded, string? Error) : IIntegrationEvent;
