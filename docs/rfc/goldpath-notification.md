# Module RFC: Goldpath.Notification (one event → one message, with evidence)

> Status: v1.0 ACCEPTED — D1–D7 approved by Ömer (2026-07-08) WITH three chat-agreed
> additions folded in: attachments live in the channel CONTRACT from day one (D1), the
> evidence row stamps the TEMPLATE HASH and requests carry an optional NotBefore (D2),
> and the evidence semantics are named honestly: **Sent means ACCEPTED BY THE CHANNEL**,
> not delivered/read — delivery-status feedback is provider-specific and deferred with a
> written trigger. SHIPPED: S1 (evidence core + channels + send run), S2 (read-only
> admin surface + queue gauges + panel + runbooks + GOLDPATH16xx), S3 (`features.notification`
> manifest word — module COMPLETE; the console rides the consolidated UI phase). Trigger: the insurance card
> (renewal notices) + the telco card (invoice-ready notifications). Scope agreed LEAN in
> chat (2026-07-08): channel seam + durable evidence + idempotent send + masking —
> campaign-scale pacing/fan-out is L4's job, not this module's. Module excellence bar
> applies: §6–§8 are load-bearing. Effort M (S1–S3), expected smaller than Bulk.

## 0. What this module is — and is NOT

One domain decision → one message to one person, with the enterprise discipline every
project rewrites badly: durable evidence ("did the customer get the notice?" answered by
QUERY), idempotent send (a retry storm cannot double-SMS), channel seams (enterprises
have their own gateways), and privacy (personal data in bodies handled deliberately).

NOT this module: mass paced campaigns (L4 `campaign` composes THIS module's channel seam
later), preference-center UI, marketing segmentation, template design tooling, push
notifications (no card demands them — deferral with trigger), in-app notification feeds.

## 1. Scope / Non-Goals

**Scope:** a transactional-notification core: typed notification requests written
durably (same database), culture-aware token templates, channel providers (email via
MailKit + webhook shipped; SMS as a seam), sending as a Goldpath.Jobs run (chunked, resumable,
repair queue), claim-before-send semantics, body retention, admin surface + ops pack.

**Non-goals (deferrals with triggers):**
- SMS/push provider packages (trigger: first project names its gateway; the seam ships
  day one — a fake channel proves the contract in tests).
- Rich template engines — Scriban/Fluid (trigger: first template that outgrows tokens;
  both are free/OSS and slot behind the same seam).
- Broker-driven send triggers (trigger: first latency-sensitive consumer case; the jobs
  poll makes notification work in NO-BROKER apps — a GmFourSimple-shaped app can notify).
- Preference/opt-out STORE (the hook is a delegate: the app answers "may I send?";
  a managed store waits for a card).
- Digest/batching ("one daily email instead of 40") — trigger: first card demanding it.

## 2. Seam Map

- **Request seam — `IGoldpathNotifier` (D2):** the app requests, never sends:
  `notifier.RequestAsync(new GoldpathNotificationRequest("policy-renewal", recipient, culture,
  tokens, dedupKey))`. The request is a ROW in the app's own database, written in the
  app's ambient transaction scope — the domain change and the notification intent commit
  together (the outbox idea, applied to human messages). `DedupKey` is REQUIRED and
  UNIQUE: the same business event requested twice lands ONCE (constructor-enforced — an
  API contract beats an analyzer here). Rendering happens AT REQUEST TIME: a missing
  token throws INTO the app's transaction (a bad notice never persists), the rendered
  subject/body ARE the evidence, and the row stamps the TEMPLATE HASH — after the body
  retention window nulls the content, hash + template key still prove exactly WHAT the
  customer was sent. Requests carry an optional `NotBefore` (quiet hours are a field and
  a send-job filter, not a policy engine — that is L4).
- **Template seam (D4):** templates registered in code (baked at registration, the
  archival/bulk builder pattern): per template key → per channel → per culture, subject +
  body with `{{token}}` replacement; missing-token render REFUSES (a half-rendered notice
  is worse than none); culture fallback chain (tr-TR → tr → invariant).
- **Channel seam — `IGoldpathNotificationChannel` (D1):** `Name` + `SendAsync(message, ct)`.
  The message CONTRACT carries attachments (name + content + content type) from day one —
  an insurance renewal attaches the policy PDF; adding this later would break every
  channel. Shipped: `email` (MailKit SMTP — MIT, config-bound host/port/credentials;
  supports attachments) and `webhook` (POST JSON to a bound URL — covers Teams/Slack/
  in-house hubs; ignores attachment content, lists names). SMS = the seam + a documented
  sample; enterprises plug their gateway.
- **Execution seam — the ladder again (D3):** the SEND runs as a `GoldpathNotificationSendJob`
  (frequent cron, chunked over Requested rows, MaxParallelChunks=1 default) — the THIRD
  jobs-riding feature; the template/CLI reuse the shared-scheduler + `JobsOptionsLines`
  seams built in bulk S3. **Claim-before-send** (MDM constraint 2, again): the chunk
  stamps `ClaimedAt` (persisted) before the channel call; a row claimed-but-unstamped
  after a crash goes to the REPAIR QUEUE ("interrupted mid-flight — confirm with the
  provider, then replay"), never silently re-sent. Transient channel failures retry with
  backoff inside the attempt budget; exhausted → Failed + repair item; the jobs
  `replay-items` verb IS the re-send verb, through `IGoldpathItemReplay`.
- **Privacy seam (D5):** bodies carry personal data BY NATURE. Two controls: (a)
  `DeleteBodyAfter(period)` per template — the evidence row (who/when/channel/state)
  survives forever, the rendered body column nulls out after the window; (b) report/log
  surfaces mask the recipient through the DataProtection catalog when the module is
  present. The opt-out hook (`MaySend` delegate, app-supplied) runs at REQUEST time and
  records a `Suppressed` state — suppression is evidence too.
- **Tenancy seam:** requests carry the tenant; queries fail closed, as everywhere.

## 3. Manifest Surface

```yaml
features:
  notification: true
```

Schema key joins WITH S3 (the rule). Drift rows: the guard pair —
`features.notification` → `Goldpath.Notification`/`AddGoldpathNotification` and → `Goldpath.Jobs`/
`AddGoldpathJobs`.

## 4. API Surface

```csharp
builder.AddGoldpathNotification<WebApplicationBuilder, OrdersDbContext>(notification =>
{
    notification.AddTemplate("policy-renewal", t => t
        .Channel("email", c => c
            .Subject("tr", "Poliçeniz {{PolicyNo}} yenilenmek üzere")
            .Body("tr", "Sayın {{Name}}, poliçeniz {{RenewalDate}} tarihinde yenilenecektir.")
            .Subject("", "Your policy {{PolicyNo}} is up for renewal")     // invariant fallback
            .Body("", "Dear {{Name}}, your policy renews on {{RenewalDate}}."))
        .DeleteBodyAfter(TimeSpan.FromDays(90)));                          // evidence stays, body goes

    notification.Email(e => { e.ConnectionName = "smtp"; e.From = "noreply@goldpath.local"; });
    notification.MaySend((request, services) => Task.FromResult(true));    // the app's opt-out hook
});

// The app requests; the module sends (same-transaction intent):
await notifier.RequestAsync(new GoldpathNotificationRequest(
    template: "policy-renewal", channel: "email", recipient: policy.HolderEmail,
    culture: "tr", tokens: new() { ["Name"] = holder, ["PolicyNo"] = policy.No, ["RenewalDate"] = date },
    dedupKey: $"renewal:{policy.No}:{period}"));

// Jobs wiring: jobs.AddGoldpathNotificationJobs<OrdersDbContext>();   // send (frequent) + body-retention purge
// DbContext:   modelBuilder.AddGoldpathNotification();
```

**Admin API** (`/goldpath/admin/notification`, S2): definitions/templates view with live state
counts + oldest-requested age, notification list/detail (tenant fail-closed, recipient
masked when DataProtection present), suppression report, send-failure list delegating
replay to the jobs console. The notification rows ARE the audit.

## 5. Analyzer Rules (GOLDPATH16xx — new block)

- **GP1601 (warning):** direct `SmtpClient`/MailKit usage in a compilation that
  references Goldpath.Notification — sending around the notifier is an EVIDENCE HOLE
  ("did the customer get it?" becomes unanswerable).
- **GP1602 (info):** a template without `DeleteBodyAfter` — rendered personal data kept
  forever should be a visible decision, not a default nobody made.

## 6. Performance (measured, not promised)

- Insurance's nightly renewal run: 10k notifications rendered + evidence-stamped against
  a no-op channel — measure rows/s (expectation: template render is string work; the
  channel dominates in production).
- Render micro-bench: token replacement at p95 (budget: sub-millisecond).
- Queue-age under a stopped channel: the backlog drains after recovery without loss or
  duplicates (the claim proof, notification-flavored).

## 7. Observability (shipped)

`Goldpath.Notification` meter: `sent_total`/`failed_total`/`suppressed_total` (per template +
channel), send duration histogram, **oldest-requested age** (the queue-health alert: a
stuck channel pages BEFORE customers call), retry counter. Grafana panel (send funnel,
failure rate per channel, queue age with thresholds) + the send run's progress rides the
jobs dashboard. Every metric lands in the panel or it does not ship.

## 8. Operational (runbook or it didn't happen)

1. Queue age climbing (stopped/misconfigured channel: check channel health, the jobs
   console run view; drain proof documented).
2. Send failures (repair queue triage; "interrupted mid-flight" rows need provider-side
   confirmation BEFORE replay — the claim semantics make double-send a decision, not an
   accident).
3. A customer says "I never got it" (the evidence query: request→claim→sent timestamps +
   channel response; body may already be retention-nulled — the metadata answers anyway).
4. Suppression disputes (the Suppressed row records the hook's verdict and when).
5. Template change discipline (templates are code → PR-reviewed, golden-tested; no
   runtime template edits BY DESIGN — a wrong notice at 10k scale is a code-review
   problem, not an ops knob).
6. Body-retention window and KVKK requests (evidence survives, body nulls; recipient
   masking on surfaces).

## 9. Test Plan

- **Unit:** template render goldens (tokens, missing-token refusal, culture fallback),
  dedup uniqueness, state machine, claim/interrupt semantics, retry/exhaustion, MaySend
  suppression evidence, EF model contract, options builder bake.
- **Mutation:** ≥ 70 break (the standard config; endpoints/DI/meter shells excluded).
- **Integration (pg):** renewal story end to end with a recording channel — request in
  the app transaction → send run (real runner) → evidence rows; poisoned channel →
  repair → replay sends ONCE; kill-window row lands in repair not re-sent; no-broker
  shape works; tenant fail-closed.
- **Bench:** §6 numbers.

## 10. Slices & DoD

- **S1** — model/evidence + notifier + templates + email/webhook channels + send job
  (claim semantics) + retention purge + integration proof + bench.
- **S2** — admin API + metrics/panel/runbooks + GP1601-1602.
- **S3** — `features.notification` schema key + template `--features notification`
  (renewal-notice sample) + CLI recipe (reuses the JobsOptionsLines seam) + drift pair +
  GM grows to ten → module closes.
- DoD: the two cards' notification lines answered; excellence-bar artifacts present;
  ledger updated; console rides the UI phase.

## 11. Decision Points (Ömer)

- **D1 — Shipped channels: email (MailKit SMTP) + webhook; SMS as a documented seam**
  (enterprises plug their own gateway; no provider lock-in shipped). Attachments are in
  the channel CONTRACT from day one. ACCEPTED.
- **D2 — `IGoldpathNotifier` with a REQUIRED unique DedupKey; requests are rows in the app
  database written in the app's transaction** (the outbox idea for human messages);
  sending around the notifier is analyzer-flagged (GP1601). Render-at-request,
  template-hash evidence stamp, optional NotBefore. ACCEPTED.
- **D3 — Sending runs as a Goldpath.Jobs run** (frequent cron; works in no-broker apps;
  repair queue + `replay-items` = the re-send story; claim-before-send per MDM
  constraint 2). Broker-driven triggers deferred with a written trigger. ACCEPTED.
- **D4 — Templates are CODE** (registered, baked, golden-tested, PR-reviewed; token
  replacement + culture fallback; missing token REFUSES; rich engines deferred).
  No runtime template editing by design. ACCEPTED.
- **D5 — Privacy: `DeleteBodyAfter` per template (evidence survives, body nulls) +
  recipient masking on surfaces via DataProtection + the `MaySend` opt-out hook with
  Suppressed-as-evidence.** ACCEPTED.
- **D6 — Retry inside the send attempt (bounded, backoff); exhausted → repair queue;
  interrupted-mid-flight NEVER auto-resends** (provider confirmation is a human step).
  ACCEPTED.
- **D7 — GOLDPATH16xx block: 1601 bypass warning, 1602 retention-less template info.** Accept?
