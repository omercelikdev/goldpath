# Goldpath.Notification

One domain decision → one message to one person, with the discipline every project
rewrites badly:

- **Evidence:** every notification is a durable row — "did the customer get the notice?"
  is a QUERY. After body retention nulls the content, the **template hash** still proves
  exactly WHAT was sent. `Sent` means **accepted by the channel** (not delivered/read —
  named honestly).
- **Idempotent:** the REQUIRED unique `dedupKey` makes a retry storm land ONCE.
- **At-most-once-or-repair:** the send run CLAIMS rows (persisted) before any channel
  call; an interrupted send goes to the repair queue for a human-confirmed replay —
  never a silent double-send.
- **Channels are seams:** email (MailKit SMTP) + webhook ship; your SMS gateway plugs in
  as an `IGoldpathNotificationChannel`. Attachments are in the contract from day one.

## Wire it

```csharp
builder.AddGoldpathNotification<WebApplicationBuilder, OrdersDbContext>(notification =>
{
    notification.AddTemplate("policy-renewal", t => t
        .Channel("email", c => c
            .Subject("tr", "Poliçeniz {{PolicyNo}} yenilenmek üzere")
            .Body("tr", "Sayın {{Name}}, poliçeniz {{RenewalDate}} tarihinde yenilenecektir.")
            .Subject("", "Your policy {{PolicyNo}} is up for renewal")
            .Body("", "Dear {{Name}}, your policy renews on {{RenewalDate}}."))
        .DeleteBodyAfter(TimeSpan.FromDays(90)));   // evidence survives, content goes
});

builder.AddGoldpathJobs<WebApplicationBuilder, OrdersDbContext>(jobs =>
{
    jobs.ConnectionName = "ordersdb";
    jobs.AddGoldpathNotificationJobs<OrdersDbContext>();   // send (frequent) + retention (nightly)
});

// DbContext: modelBuilder.AddGoldpathNotification();  modelBuilder.AddGoldpathJobs();
// Config:    Goldpath:Notification:Email { Host, Port, UseSsl, User, Password, From }
```

## Request (the app never sends directly — GP1601)

```csharp
await notifier.RequestAsync(new GoldpathNotificationRequest(
    template: "policy-renewal", channel: "email", recipient: policy.HolderEmail,
    culture: "tr",
    tokens: new Dictionary<string, string> { ["Name"] = holder, ["PolicyNo"] = policy.No, ["RenewalDate"] = date },
    dedupKey: $"renewal:{policy.No}:2026-08")
{
    NotBefore = tomorrowMorning,          // quiet hours are a field, not a policy engine
    Attachments = [new("policy.pdf", "application/pdf", pdfBytes)],
}, ct);
```

Rendering happens AT REQUEST TIME: a missing token throws into YOUR transaction — a bad
notice never persists. The `MaySend` hook records refusals as `Suppressed` rows —
suppression is evidence too.

Ops surface (admin API, panel, runbooks) ships in S2; the send run is visible in the
JOBS console today.
