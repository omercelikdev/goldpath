# RFC: the delivery cycle — one flow, two audiences

**Status:** proposed
**Date:** 2026-09-08
**Supersedes:** the D1 decision of [goldpath-skills-v1](goldpath-skills-v1.md) ("v1 set = the four
above"), which this RFC widens rather than replaces.
**Constitution:** ADR-0005 (every standard ships with its verifier), ADR-0012 (product modules
on the platform).

---

## 1. Scope / Non-Goals

**Scope.** One delivery cycle, written once, that holds for every change in the family —
whether the change is a defect or a feature, and whether it lands in an application generated
from the templates or in the libraries that generate it. It ships as the agent layer
(`.claude/`) so an agent runs the sequence instead of remembering it, and as gates so the
sequence cannot be skipped silently.

**Non-goals.** Not a methodology document nobody reads. Not a replacement for the ADRs — the
cycle enforces the SEQUENCE and defers every rule to the documents that own it. Not a change to
what the analyzers or the engine check; those already exist and the cycle points at them.

## 2. The gap this RFC exists to close

The family has two audiences and serves one.

**The adopter** — someone building an application with Goldpath — is served well. A generated
app is born with three skills, the breaker agent, conventions, the MCP engine and two in-loop
hooks, one of which refuses to let an agent end its turn on a red build.

**The maintainer** — someone changing the libraries themselves — has nothing. Measured on
2026-09-08:

| Repository | Agent layer today |
|---|---|
| goldpath | one skill, and it is an MR reviewer, not a cycle; no hooks |
| mockifyr | none |
| qorpe/ui | none |
| mediant | none |
| specdrift, specanchor | no `.claude` at all |

So the accelerator holds its customers to a discipline it does not run on itself. That is the
gap, and it is also why the cycle has drifted into three different shapes: the goldpath
template's, praxis's, and one hand-written for qorpe.sync.

## 3. The cycle

Nine steps. A defect and a feature differ only in step 1; everything after is identical, which
is why this is one flow and not two.

1. **Prove the cause, or prove the need.**
   *Defect:* evidence, not a hypothesis — logs, data that sizes the problem, the report's own
   attachments. If two explanations fit, rule one out **in writing** before building on the
   other. *Feature:* who asked, what evidence, what done means, and the part that gets
   skipped — what is deliberately out of scope.
2. **Agree before building.** Put the diagnosis, or the approach and its trade-off, and wait.
   Record the outcome where the repository keeps decisions when the choice is expensive to
   reverse.
3. **Read what must not break.** The repository's invariants (§5) and the ADRs the change
   touches. A change that contradicts one is a conversation, never a workaround.
4. **Write the failing test — then prove the test.** Watch it fail for the right reason, then
   put the fault back and confirm it goes red again. A test green on both sides of a fix is
   worse than no test, because it advertises a guarantee it does not provide.
5. **Choose the layer that can actually fail.** A unique-index collision cannot be reproduced
   in memory; a layout defect cannot be reproduced without layout. The table in §6 says what
   each layer *cannot* tell you, which is the half that makes it decidable.
6. **Implement, then consult the engine.** The smallest change that passes. "Done" without a
   clean engine run is not done — §4 says what the engine is for each audience.
7. **Run it for real and measure.** Automated green is not seen-working. Where the change has
   a user-facing surface, drive it: open the screen, sign in if it asks, use it as a person
   would, and read the computed value, the payload, the row, the message — not the impression.
   Where it has none, call the real endpoint or trigger the real job and read what it produced.
   "It looks right" is not a result in either case.
8. **Watch what you did not intend to change.** Does an existing test encode the old contract?
   Did a field's meaning change while its name did not? Does removing a path leave a stale
   translation, screen or job behind? Update every document this change made untrue, in the
   same change.
9. **Land with evidence.** The pull request carries what ran and what it produced — including
   the step you skipped and why, because skipping is a decision and an unstated gap is the
   expensive kind. A green local run is not a green pipeline: if the change touches CI or
   reads outside its own project, run the job the way CI runs it before pushing.

## 4. Spec-driven on both sides

Both audiences already have a machine-checkable specification and a drift gate. The cycle's
step 6 names them so "consult the engine" is concrete rather than a slogan.

| | The spec | The gate |
|---|---|---|
| **Application** | the committed OpenAPI in `specs/` and `.goldpath/manifest.yaml` | `spec_validate` + `spec_drift` (MCP), and SPEC0212 — the committed contract must equal the built one |
| **Library** | `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` and the analyzer release files | RS0016 (public API not in the file), RS2000/RS2007 (a rule outside a release file), plus the repository's own ledger and freshness gates |

The symmetry is the point: a library's public surface IS its contract, and it already fails a
build when it drifts. What was missing is a cycle that tells the maintainer to look.

## 5. Domain knowledge is part of the layer

A cycle that does not know what must not break can only protect syntax. Every repository in the
family gains one short document that names its invariants with stable identifiers, and step 3
makes reading it mandatory before behaviour changes.

- For a generated application this is the manifest plus the business rules the app itself
  declares; the template ships the file with the walking skeleton's invariants as worked
  examples, so an adopter fills a shape rather than inventing one.
- For a library it is the guarantees its public surface makes.
- The identifier matters: an invariant with an id can be cited by the test that proves it, and
  a test that names an invariant explains itself to the next reader.

## 6. What ships, and where

The layer is `.claude/` and it is copied to five places today. This RFC adds a sixth audience —
the library repositories — and a gate that keeps the copies honest.

| Artefact | Adopter (templates, samples, generated repos) | Maintainer (library repositories) |
|---|---|---|
| `<repo>-change` skill | new: the nine steps, deferring rules to the repo's documents | new: same nine steps |
| `goldpath-feature` | gains steps 4, 7, 8 | — |
| `goldpath-defect` | new | new |
| `goldpath-test-gen` | unchanged (its context diet is already the strongest rule we have) | adopted as-is |
| `breaker` agent | unchanged | adopted |
| hooks (`stop-gate`, `format-touched`) | unchanged | **adopted — the libraries do not run them today** |
| invariants document | template ships the shape | per repository |

The test-layer table the cycle's step 5 refers to:

| Layer | Use it for | What it cannot tell you |
|---|---|---|
| Unit | pure logic, mapping, decision rules | anything the database or a layout decides |
| Integration | constraints, unique indexes, transactions, concurrency — on real infrastructure | anything a user sees |
| End-to-end | routing, the rendered screen, what the reader actually gets | behaviour under load or concurrency |
| Mutation | whether the tests above would notice if the code were wrong | whether the behaviour is the right one |

## 7. Gates

- **`skills-parity.sh`** (new). The files that must be byte-identical across
  `templates/goldpath-solution`, `templates/goldpath-worker` and `samples/corpay` are, and the
  ones that are deliberately shape-specific (`conventions.md`, the worker's breaker) are listed
  as exceptions with their reason. Today the claim in `skills/README.md` that CorPay carries a
  byte-identical copy is TRUE and UNPROTECTED — and api-portal's `stop-gate.sh` has already
  drifted from the template's.
- **Evals.** Every new or changed skill brings its `evals/skills/<name>/` scenario; acceptance
  is by outcome, never by prompt text (skills RFC D5).
- The worker template is missing `goldpath-feature` and `goldpath-test-gen` entirely. A worker
  is born with one skill today; the parity gate makes that visible rather than incidental.

## 8. Test plan

- The parity gate is proven by planting a drift in a copy and confirming red.
- Each skill's eval runs its scenario end to end on a generated app (the `evals-acceptance`
  nightly job).
- The cycle's own claim — that it holds for a defect as well as a feature — is proven by
  running `goldpath-defect` against a planted fault in the sample, with the failing test
  written before the fix and proven to fail.

## 9. Definition of done

- [ ] The nine steps exist once, in the template's layer, and every skill defers to them
      instead of restating them.
- [ ] `goldpath-defect` ships; `goldpath-feature` gains the proven-test, run-it-for-real and
      as-is steps.
- [ ] The worker template carries the same skill set, in its own shape.
- [ ] `samples/corpay` matches the template, and `skills-parity.sh` fails when it stops
      matching.
- [ ] goldpath, mockifyr, qorpe/ui and mediant carry the maintainer layer, including the hooks
      they currently ship to others and do not run themselves.
- [ ] Every repository names its invariants with identifiers, and the cycle's step 3 cites them.
- [ ] The CHANGELOG and the upgrade guide carry it, because the templates change is a train
      decision.

### Decisions

- **D1 — One cycle, two audiences.** Not two methodologies. The steps are identical; only the
  first differs between a defect and a feature.
- **D2 — The skill enforces the sequence and carries no rules of its own.** Rules live in the
  documents that own them. This is the anti-drift rule praxis proved: a skill that restates a
  rule is a second source of truth waiting to disagree.
- **D3 — Step 7 is conditional by surface, not optional.** Where there is a user-facing
  surface, driving it is mandatory. Where there is none, calling the real thing and reading its
  output is mandatory. What is never acceptable is asserting that it works.
- **D4 — The libraries adopt what they ship.** Including the stop gate. An accelerator that
  exempts itself from its own discipline is making an argument against that discipline.
- **D5 — Parity is a gate, not a convention.** Copies that must be identical are checked.
