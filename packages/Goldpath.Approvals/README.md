# Goldpath.Approvals

Human approval workflows as a composable Ring B module: amount-laddered authority chains
**declared as data**, four-eyes and maker-checker enforcement, bounded delegation, deadline
escalation, and the worklist — with every lifecycle step audited and published as
integration events. This module replaces the e-mail approval thread, not the humans in it.

```csharp
builder.AddGoldpathApprovals(approvals => approvals
    .AddLadder("credit-limit", ladder => ladder
        .Rung("expert", upToInclusive: 1_000_000m, escalateAfter: TimeSpan.FromHours(8))
        .Rung("deputy-manager", 5_000_000m, TimeSpan.FromHours(8))
        .Rung("manager", 15_000_000m, TimeSpan.FromHours(8))
        .TopRung("general-manager", TimeSpan.FromHours(24))));
```

- **Request** — `engine.RequestAsync("credit-limit", subject, amount, requestedBy)` routes
  the amount to its rung and starts the trail.
- **Decide** — `engine.DecideAsync(id, decidedBy, deciderRole, granted, reason)` enforces
  four-eyes (the requester may never decide their own request) and the rung's role.
  Refusals are values (`GoldpathApprovalDecisionOutcome`), not exceptions.
- **Delegate** — bounded window, depth one (a delegate cannot re-delegate; the cycle guard
  is structural).
- **Escalate** — `EscalateOverdueAsync()` moves overdue requests one rung up; overdue at
  the top rung expires. Schedule it through the Jobs module.
- **Worklist** — `WorklistAsync(identity, role)`: what this person may decide, oldest first.

Events (`GoldpathApprovalRequested/Granted/Rejected/Escalated/Expired`) carry the
`IIntegrationEvent` marker and publish through the messaging seam when a broker is
composed — and stay silent when not.

State lives behind `IGoldpathApprovalStore`; the in-memory store ships for tests and
single-node hosts, a database-backed store composes through the seam.

This is a saga's counterpart, not its competitor: a compensating flow orchestrates
SYSTEMS and may request an approval as one of its steps; Approvals never drives system
steps itself (RFC D3).
