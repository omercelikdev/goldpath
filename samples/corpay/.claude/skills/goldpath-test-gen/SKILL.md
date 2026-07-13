---
name: goldpath-test-gen
description: Generate tests FROM THE SPEC, never from the implementation — the independent tester of foundation §8.2. Use when asked to write/extend tests for a feature that has a contract.
---

# goldpath-test-gen — spec-derived tests, implementation-blind

A test derived from the code it validates converges to zero value. This skill exists to
keep the tester's context CLEAN.

## The context diet — this is the whole point

You MAY read:
- `specs/` (the committed contracts), `.goldpath/manifest.yaml`
- `.claude/conventions.md`, this skill
- the TEST projects (what exists, naming, harness patterns)
- the PUBLIC wire surface (calling the running app / the exported OpenAPI)

You MUST NOT open:
- `src/**/Features/**` (any handler/validator/entity implementation)
- any diff of the implementation you are testing

If you cannot write a test without peeking, the SPEC is underspecified — stop and report
the gap instead of peeking. That report is a more valuable output than a coupled test.

## Hard steps

1. Derive cases from the contract: every documented status code, every error shape, the
   example tables if present. Boundaries first (the spec's min/max/enum edges).
2. Property-based (CsCheck) where the surface is algorithmic (parsers, key/cursor formats,
   grammars) — hand-picked examples are the weak form.
3. Behavioral assertions only: wire-visible outcomes (status, body shape, list contents,
   emitted events via the smoke harness) — never internal state.
4. Run the suite; your tests must be green against the CURRENT implementation, or
   explicitly delivered as a failing repro with the spec line it contradicts.
5. `spec_drift` (MCP `specdrift`) after adding files — test projects are part of the repo
   the manifest describes.

## Output

New/changed test files + a one-paragraph note: which spec sections are covered, which are
UNCOVERABLE as specified (the gap list). The gap list feeds the breaker agent.
