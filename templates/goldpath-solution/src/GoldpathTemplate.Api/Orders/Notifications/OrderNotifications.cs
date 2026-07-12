namespace GoldpathTemplate.Api.Orders.Notifications;

/// <summary>
/// Notification vocabulary for the Orders slice (features.notification sample). Template
/// keys are WIRE CONTRACTS: the evidence rows carry them forever — name them like APIs.
/// </summary>
public static class OrderNotifications
{
    /// <summary>The order-confirmed notice (registered in Program.cs).</summary>
    public const string OrderConfirmedTemplate = "order-confirmed";

    /// <summary>
    /// The dedup key for one order's confirmation notice: the SAME order confirmed twice
    /// (a retry, a replayed event) must land as ONE notification.
    /// </summary>
    public static string OrderConfirmedKey(string orderReference) => $"order-confirmed:{orderReference}";
}

// Requesting the notice from a handler (the app requests, the module sends — GP1601):
//
//   await notifier.RequestAsync(new GoldpathNotificationRequest(
//       template: OrderNotifications.OrderConfirmedTemplate,
//       channel: "email",
//       recipient: customerEmail,
//       culture: "tr",
//       tokens: new Dictionary<string, string>
//       {
//           ["Reference"] = order.Reference,
//           ["Amount"] = order.Amount.ToString("C", culture),
//       },
//       dedupKey: OrderNotifications.OrderConfirmedKey(order.Reference)), ct);
