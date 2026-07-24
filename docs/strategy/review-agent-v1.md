# Goldpath Review Agent v1 — Rule Set

> Definition of the "review agent" link in the Foundation 6.4 chain: a second AI set of eyes
> that runs on every MR BEFORE human review. Status: v1 SHIPPED 2026-07-10 — rule set:
> `.claude/skills/goldpath-review/SKILL.md`, runner: `scripts/review-agent.sh` (PR mode +
> `--local <diff>` eval mode). The runner is MANUAL today — there is no CI step; one lands
> when runner minutes carry gh + claude (deferred, tracked in ai-sdlc-status). Calibration
> telemetry (section 5) starts as the reply-thread convention the posted comment asks for;
> automated fate-tracking rides the CI enablement.

---

## 0. Position and Authority

- Its place in the chain: `...mutation threshold → REVIEW AGENT → human review → merge`
- **It does not block on its own** — it produces findings, applies labels, and the human
  decides. (Exception: the two "hard-stop" classes below, which are things that could never
  pass without human approval anyway.)
- **Re-running deterministic checks is FORBIDDEN:** it reports nothing that the
  analyzer/lint/format/Spec Engine already catches (noise = loss of trust). Its job is what
  the machine *cannot express as a rule* but can recognize as a pattern.

## 1. Finding Classes (what it flags)

| Class | Examples | Label |
|---|---|---|
| **R1 Spec-code semantic mismatch** | Endpoint deviates from the behavior described in the spec (the schema matches but the meaning does not — the layer drift cannot catch); logic contradicting the approved example table | `review:spec-mismatch` |
| **R2 Domain rule violation** | Touched code contradicts one of the relevant BC's `approved` BR-* rules; naming outside the ubiquitous language (an invented name where a term exists in the glossary) | `review:domain` |
| **R3 Suspicious logic** | Swallowed exception/CancellationToken, empty catch, out-of-scope behavior change (a side change the MR title does not promise), dead/unreachable branch, leftover TODO/debug code | `review:logic` |
| **R4 Security pattern** | Logging PII (against the dataProtection catalog), string-concatenated SQL, constants that look like secrets, a write path with authorization checks skipped | `review:security` |
| **R5 Test quality** | Assertion-free/meaningless test, test that copies the prod code (tautology), edge-case anchor left untested despite the type being touched | `review:test-quality` |
| **R6 Simplicity/altitude** | Obvious duplication (a hand-written equivalent while an existing Goldpath primitive/Mediant behavior is available), unnecessary abstraction | `review:simplify` |

**Hard-stop (label + merge block, resolved by a human):** a high-confidence secret/PII finding
in R4; a direct contradiction with an `approved` rule in R2. Everything else is
suggestion-level.

## 2. What It Does NOT Flag (protecting the false-positive budget)

- Style/format/naming mechanics (the job of the analyzer + dotnet format)
- "This is how I would have written it" preferences — alternatives that do not change behavior
- Anything deterministic tools already paint red
- Claims based on rules/specs in `draft` status (only `approved` ones are used as reference)

## 3. Input Context (what the agent is given)

MR diff + the manifest of the touched service + the touched spec files + the MR
description. **The whole repo is not provided** — context economy serves both cost and
accuracy. Domain memory joins this list when it exists (Phase 2/3 — `domain-memory-v1.md`
is a draft); until then the runner passes none, so the R2 finding class is dormant by
construction, not by accident.

## 4. Output Contract

Finding = `{class, file:line, one-sentence claim, evidence (BR-id / spec anchor / pattern
name), confidence (high|medium), suggested action}`. One consolidated comment on the MR +
labels; if there are no findings, a silent approval comment ("R1-R6 scanned, no findings" —
so it is not mistaken for "not scanned").

## 5. Calibration Loop (via telemetry)

- The fate of every finding is tracked: `accepted | dismissed` (appended to the pr event)
- **Dismiss rate per class > 40% (monthly) → that class's prompt/threshold gets revised** —
  the rule set itself lives under evals too (part of the skill eval harness)
- Target: average findings per MR < 3 (noise control), accepted rate > 60%
