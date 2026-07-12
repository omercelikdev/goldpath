# Goldpath.Notification — runbooks (notification RFC §8: runbook or it didn't happen)

Views live at `/goldpath/admin/notification` (READ-ONLY by design: requesting belongs to the
app, re-sending to the JOBS console). The notification rows ARE the audit; recipients are
MASKED on every admin surface.

## 1. Queue age climbing (the stuck-channel alert)

- `goldpath_notification_oldest_requested_age_seconds` pages. `GET /templates` shows which
  template's queue is aging; `GET /notifications?state=requested` lists the backlog.
- The send run fires every ~30 s: check the jobs console — is `GoldpathNotificationSendJob`
  paused? Fleet down? Then the CHANNEL: SMTP host reachable? Webhook URL alive? The
  channel config keys are in the package README.
- Recovery drains automatically — the claim query picks the oldest first; no verb needed.

## 2. Send failures / repair triage

- `GET /failures` — every exhausted notification with its Detail (the LAST channel
  error) and attempt count. The repair queue itself (and the `replay-items` verb) lives
  in the JOBS console under the send run.
- "interrupted mid-flight" rows are a SPECIAL case: the claim was persisted but the
  outcome never was — the provider MAY have accepted. Confirm on the provider side
  (the MIME Message-Id carries the notification id) BEFORE replaying; the claim
  semantics exist so a double-send is a decision, not an accident.
- Replay resets the attempt budget and routes through the same channel; success flips
  the row to Sent and closes the repair item.

## 3. "The customer says they never got it"

- Find the row: `GET /notifications?template=...` (dedup key is the business identity —
  `renewal:P-42:2026-08` finds it exactly). The evidence answers: Requested-at,
  Claimed-at, Sent-at (= channel ACCEPTED — accepted ≠ delivered), attempts, channel.
- Body already retention-nulled? The TEMPLATE HASH still proves exactly which registered
  text version rendered; the culture column says in which language.
- Sent but not received → the trail continues on the PROVIDER side (bounce logs, spam
  filters); the Message-Id `<notificationId@goldpath>` is the join key.

## 4. Suppression disputes

- `GET /suppressions` — every MaySend refusal with its when. Suppression is EVIDENCE:
  "why didn't the customer get the notice?" may legitimately answer "your opt-out said
  no on July 3rd". The hook is app code — its policy questions route to the app team.

## 5. Template change discipline

- Templates are CODE: PR-reviewed, golden-tested, hash-stamped. There is NO runtime
  template edit BY DESIGN — a wrong notice at 10k scale is a code-review problem, not an
  ops knob. After a deploy, new sends stamp the NEW hash; the old rows keep the old one —
  the transition is visible in the evidence.

## 6. Retention & KVKK

- Rendered bodies + attachment CONTENT null out after each template's `DeleteBodyAfter`;
  the evidence row, attachment NAMES and the template hash survive forever.
- An erasure request against notification evidence: recipient masking already covers the
  admin surface; store-level erasure of the recipient column follows the archival
  module's erasure discipline if the app archives notification rows (long-term evidence
  belongs there).
- A template with NO `DeleteBodyAfter` keeps personal data indefinitely — GP1602 makes
  that visible at build time; treat it as a decision, not a default.
