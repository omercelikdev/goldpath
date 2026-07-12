# Goldpath Delivery Telemetry and Delivery Report v1 (Working Draft)

> The concrete realization of Foundation 6.5: the evidence machine behind the sentence
> "this would normally take X, we did it in Y with this." The data comes out of the product
> itself — it is not collected in Excel after the fact.
> Status: v0.1 draft (2026-07-03)

---

## 0. Principles

1. **Privacy first:** Events contain NO code content, domain data, or PII — only numbers,
   durations, IDs, and rule codes. The data stays in the customer's own repo hosting; anonymized
   aggregate metrics flow back to the company only under contract.
2. **Zero extra infrastructure (v1):** The air-gapped network reality — events are CI job
   artifacts (JSONL); a nightly aggregation job produces and commits
   `docs/delivery/metrics.json` + the report.
   v2: optional OTel export → enterprise dashboard.
3. **Measurement is a side effect:** No event is entered by hand; skills, the Spec Engine, CI,
   and the repo-hosting API record events that already happen. Extra work for measurement = the
   death of measurement.
4. **Report = a generated document** (foundation section 4, "generated" class) — never edited
   by hand.

## 1. Event Model (JSONL; shared envelope + kind body)

```json
{ "ts": "2026-07-03T14:22:05Z", "kind": "skill-run", "project": "OrderPlatform",
  "service": "ChequeService", "actor": "skill:add-feature", "correlation": "MR!184", "data": { } }
```

| kind | data fields | Source |
|---|---|---|
| `skill-run` | skill, outcome (success\|retry\|abandoned), durationSec, artifacts (mr/spec/docs), model | skill runner |
| `spec-lifecycle` | spec, from→to (draft→review→approved…), gate (business\|dev\|ops) | Spec Engine + MR hook |
| `ci-stage` | stage, outcome, durationSec, findings[{ruleId,count}] (guardrail catches) | CI template |
| `mr` | opened/merged ts, humanFindings (review comment count), aiAuthored (bool), humanCommitsOnAiMr | repo-hosting API (nightly pull) |
| `deploy` | env, version, outcome | CD stage |
| `suppression` | type (test-skip\|lint-suppress\|deviation), ruleId, justification, approver | Spec Engine + CI |
| `incident` | severity, linkedService, escapedFrom (which stage missed it) | ops process (the one manual-entry exception) |

## 2. Derived Metrics (KPI set — the report is rendered from these)

**Speed:** spec-approved → prod lead time · MR open→merge duration · time-to-first-deploy
(repo init → first prod) · features/week
**Quality:** human review findings per MR · guardrail catch distribution (by ruleId —
"what do we catch most" = standards training signal) · mutation score trend · nightly
red rate · escaped defect count (incident → stage trace: which gate missed it)
**AI effectiveness:** skill success rate · AI-authored MR ratio · human intervention rate on
AI MRs (a human commit = a signal of where the skill fell short) · average duration per skill
**Honesty:** active suppression list (with justifications) · number of skipped stages

## 3. Delivery Report Template (monthly + per-release; auto-rendered)

```markdown
# Delivery Report — <Project> · <period>
## Summary (headline)
5 services, 42 features in 8 weeks · spec→prod median 6.5 days · defects escaped to prod: 0
## Speed            → table + trend (Δ vs. previous period)
## Quality          → guardrail top-10 rules, mutation trend, nightly health
## AI Effectiveness → skill success rates, human intervention points
## Honesty          → active suppressions, skipped stages, open risks
## Comparison       → enterprise baseline / previous period (function-point reference if available)
```

Rule: the report delivers **good and bad news together** — the Honesty section cannot be
removed (if it could be removed, the report would be a marketing brochure; auditors and
management read the same document).

## 4. Baseline Strategy

- Pilot (own team): real data from 2-3 past services (back-derived from git/MR history)
- Customer: existing lead times before the transformation are measured at the start of the
  contract (no "after" claim without a photograph of the "before") — a standard step of the
  sales process.
```
