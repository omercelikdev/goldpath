# RFC: Goldpath.Ai — the opt-in runtime AI module

> Status: **v1.0 ACCEPTED** (2026-07-26) — D1–D4 approved by Ömer as recommended:
> D1 ADR-0011 adopted (docs/adr/ADR-0011) · D2 the four-capability cut stands, document
> AI/RAG/ML serving stay OUT · D3 the tool registry ships ON when `features.ai` is on ·
> D4 implementation opens AFTER the UI phase (U1–U4 first).

## 0. Why (and why NOT more)

Goldpath's AI-native story today is DEVELOPMENT-time (ADR-0006: skills, MCP, spec
engine, review agent — no AI inside shipped packages). Enterprise buyers evaluating an
"AI-native" accelerator also ask about RUNTIME: can the application itself use a model,
governed? The answer must be a designed, opt-in seam — not a rewrite of the dev-time
philosophy and not a product-feature grab.

The module ships the four capabilities that are genuinely FRAMEWORK territory. Everything
else (document intelligence, RAG/copilot products, ML scoring, BPMN AI-task engines) is
deliberately out: compose, don't own.

## 1. Scope / Non-Goals

**In:** one opt-in module family behind `features.ai` (compile-time composition — off
means the code does not exist in the app, ADR-0001 discipline unchanged):

1. **Model gateway** — `AddGoldpathAi(...)` composes `Microsoft.Extensions.AI`
   (ADR-0003: configure, don't wrap): provider-agnostic `IChatClient` registration,
   pinned model id from configuration, token/cost telemetry tagged with the goldpath
   correlation id. No prompt library, no agent runtime.
2. **Admin tool registry** — the FROZEN admin contract auto-published as MCP tools:
   every mapped `/goldpath/admin/*` surface becomes a typed tool (list fleets, get run,
   trigger job...), served by the management head. Authorization is UNCHANGED: the tool
   call carries the caller's token; the ops floor, R1 tenant scoping and the audit rows
   apply exactly as they do to HTTP — the registry is a projection of the contract, not
   a second door.
3. **AI decision record** — one table + writer: model id + prompt hash + input digest +
   confidence + the human's action + final outcome, correlation-stamped. The auditor
   gets a decision FILE, not a shrug. Rides AuditTrail conventions; masked fields per
   DataProtection classification.
4. **Confidence gate** — a primitive, not a workflow engine:
   `GoldpathAiGate.Evaluate(confidence, threshold)` → `AutoProceed | NeedsApproval`,
   with the approval path being the ADOPTER's existing four-eyes muscle. The threshold
   is configuration; changing it is an audited admin verb.

**Out (non-goals, written):** document AI / OCR, embeddings & RAG, ML model serving,
champion/challenger ops, BPMN/workflow AI steps, agent runtimes, any bundled prompt
content. Adopters compose those on top of the gateway.

## 2. Constitutional impact — proposed ADR-0011

ADR-0006 stays: **core packages never reference AI**; skills/dev-layer unchanged.
ADR-0011 (one paragraph): *runtime AI is permitted ONLY inside the opt-in `Goldpath.Ai`
module family, disabled by default, composed from vendor-neutral abstractions
(`Microsoft.Extensions.AI`), with every AI-influenced decision recorded and every
model-boundary crossing carrying the same tenant/authorization context as HTTP.* No
other package may take an AI dependency.

## 3. Manifest Surface

```yaml
features:
  ai:
    enabled: true          # default false — absent means the module does not exist
    provider: openai-compatible   # the M.E.AI provider id; endpoint/key via config
    decisionRecord: true   # default true when ai is on; off is a written opt-out
    toolRegistry: true     # publish the admin contract as MCP tools
```

## 4. API Surface (sketch — freezes only at ACCEPT)

- `AddGoldpathAi(this IHostApplicationBuilder, Action<GoldpathAiOptions>?)`
- `MapGoldpathAiTools(this IEndpointRouteBuilder, prefix = "/goldpath/ai/mcp")` (management head)
- `IGoldpathAiDecisionRecorder.RecordAsync(GoldpathAiDecision, ct)`
- `GoldpathAiGate.Evaluate(double confidence, GoldpathAiGateOptions)`

## 5. Analyzer Rules

- `GP19xx-1` (error): an AI package reference (`Microsoft.Extensions.AI*`, provider SDKs)
  in a project whose manifest does not enable `features.ai` — ADR-0011's verifier.
- `GP19xx-2` (warning): an `IChatClient` call site with no reachable
  `IGoldpathAiDecisionRecorder` usage in the same feature slice — an AI decision without
  its record.

## 6. Ops Package

Dashboard: tokens/cost per feature slice, gate outcomes (auto vs approval), decision-record
write failures. Runbook: model outage = the gate's `NeedsApproval` path IS the fallback —
no model, no auto-proceed, humans keep working.

## 7. Test Plan

Unit: gate thresholds (boundary/NaN/negative confidence — edge-case checklist applies);
decision-record masking. Integration: tool registry serves the frozen contract against a
real app, ops floor + R1 enforced through the MCP path (a foreign-tenant tool call is
refused exactly like HTTP). GM impact: one new shape `GmAiOn` (module present, gateway
stubbed) proving compile-time composition both ways.

## 8. DoD

Module + analyzers + ops pack + GM shape green + this RFC's D-points resolved + ADR-0011
committed + `ai-sdlc-status.md` gains the runtime row with honest statuses.

## Decision points (Ömer) — RESOLVED 2026-07-26, all as recommended

- **D1** — Adopt ADR-0011 as scoped above (core stays clean; runtime AI only in the
  opt-in module)?
- **D2** — The four-capability cut: agree that document AI/RAG/ML serving stay OUT?
- **D3** — Tool registry default: ships ON when `features.ai` is on (recommended — it is
  the differentiator), or its own toggle default-off?
- **D4** — Timing: implementation opens after the UI phase (recommended — U-phases are
  the current train), or interleaved before U2?
