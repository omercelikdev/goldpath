using Goldpath;

namespace GoldpathTemplate.Api.Orders.Campaigns;

/// <summary>
/// Campaign vocabulary for the Orders slice (features.campaign sample). Type keys are
/// WIRE CONTRACTS: campaign rows and broker messages carry them — name them like APIs.
/// Types ship as CODE through PRs; operators create INSTANCES over them at runtime
/// through the audited admin API (`POST /goldpath/admin/campaign`).
/// </summary>
public static class OrderCampaigns
{
    /// <summary>The dormant-customer winback campaign type (registered in Program.cs).</summary>
    public const string WinbackType = "order-winback";
}

/// <summary>
/// One winback target — the payload persisted per item row and handed to the handler.
/// Keep it SMALL: at L4 scale this JSON exists once per target.
/// </summary>
public sealed record DormantCustomerTarget(long OrderId, string Reference);

/// <summary>
/// Executes ONE winback target. The item row was CLAIMED before this runs — a broker
/// redelivery cannot double-execute it. Do NOT call SaveChanges here (GP1702): outcomes
/// flow through the batching sink. Throwing marks the item Failed with your message —
/// it lands in the repair queue for a human-confirmed replay.
/// </summary>
public sealed class WinbackHandler : IGoldpathCampaignItemHandler<DormantCustomerTarget>
{
    /// <inheritdoc />
    public Task ExecuteAsync(DormantCustomerTarget target, GoldpathCampaignItemContext context, CancellationToken cancellationToken)
    {
        // YOUR side effect per target goes here — typically a notification request
        // (the notifier's dedup key makes a campaign retry safe) or a provider call.
        // context.Replay is true when an operator replays a repaired item.
        return Task.CompletedTask;
    }
}
