# Goldpath Domain Memory v1 — Format and Templates (Working Draft)

> Detail of Foundation 3.1 and 6.3. Lives in the customer repo (knowledge layer 3);
> the template generates the empty skeleton, skills fill it in (DoD), the Spec Engine lints its structure.
> Status: v0.1 draft (2026-07-03)

---

## 0. Design Principles

1. **Human-first, machine-friendly:** Markdown + YAML frontmatter. An analyst can read and
   correct it; skills parse the frontmatter and the fixed headings. There is NO separate
   "AI format" — what the human reads and what the AI reads is the same file (the domain
   counterpart of the CLAUDE.md principle).
2. **No knowledge without evidence:** Every record requires a `source` (analyst |
   reverse-engineer | incident | spec) and `evidence` (spec file, legacy code reference,
   approval MR, statutory article). A record without evidence stays in `draft` status and
   does not enter test generation.
3. **One rule per file:** MR diffs stay small and meaningful; the AI loads only the relevant
   context (context economy); two skills can touch different rules at the same time
   (no conflicts).
4. **Status lifecycle matches the specs:** `draft → review → approved → deprecated`.
   A rule that is not `approved` cannot be an input to test-gen or generate.
5. **The TR↔EN bridge is first-class:** the business language is Turkish, the code/repo is
   English (the language rule). The ubiquitous language glossary carries the TR business
   name ↔ EN code name mapping for every term; the Spec Engine uses this glossary in its
   naming validation.

---

## 1. Folder Layout (convention — no configuration)

```
docs/domain/
├── context-map.md                     # solution level: BCs + relationships (single file)
└── <bounded-context>/                 # matches manifest.boundedContext exactly
    ├── overview.md                    # purpose, scope, owner, promise to the outside
    ├── language.md                    # ubiquitous language: TR ↔ EN glossary
    ├── edge-cases.md                  # type-based boundary catalog (test-gen checklist)
    ├── integrations.md                # external system BEHAVIOR notes (what the spec can't express)
    └── rules/
        ├── _index.md                  # generated summary table (never edited by hand)
        └── BR-001-<slug>.md           # one rule per file
```

Decision rationale: `docs/domain/`, not `.goldpath/` — this is not Goldpath config; it is part of the
project's living documentation (the "living context" class in foundation section 4).

---

## 2. File Templates

### 2.1 `context-map.md`
```markdown
---
status: approved
updated-by: authoring-skill        # last writer
---
# Context Map — <SolutionName>

| Bounded Context | Responsibility (one sentence) | Service(s) | Owner |
|---|---|---|---|
| cheque-management | Cheque lifecycle (issuance→clearing→protest) | ChequeService | team-cheque |

## Relationships
- cheque-management → core-banking : **customer/supplier** (ACL in place — see integrations)
- cheque-management → notification : **published events** (asyncapi: cheque-events.yaml)
```

### 2.2 `language.md` — TR↔EN glossary
```markdown
---
status: approved
---
# Ubiquitous Language — cheque-management

| TR (business language) | EN (code name) | Definition | Code type |
|---|---|---|---|
| Keşide | Issuance | Drawing up a cheque and handing it to the bearer | `ChequeIssuance` |
| Karşılıksız | Bounced | Status of a cheque with no covering funds in the account | `ChequeStatus.Bounced` |
| Protesto süresi | ProtestPeriod | Legal objection window after presentment | `ProtestPeriod` (VO) |
```
Rules: code names may not deviate from this table (the Spec Engine's V8 naming check reads
this glossary); new terms are added as drafts by `authoring`/`reverse-engineer`, the analyst
approves them.

### 2.3 `rules/BR-001-<slug>.md` — business rule (test-gen's input)
```markdown
---
id: BR-001
title: Presentment period — same city
status: approved                    # draft | review | approved | deprecated
source: reverse-engineer            # analyst | reverse-engineer | incident | spec
evidence:
  - legacy: CHQ_PKG.check_presentment (lines 340-388)   # source evidence
  - legal: TTK m.796                                    # statutory reference (if any)
  - approval: MR!142                                    # business approval trail
specs: [specs/cheque-api.yaml#/paths/~1cheques~1{id}~1present]
edge-cases: [Date.business-day, Date.year-end]          # anchors in edge-cases.md
---
# Rule
If a cheque is to be presented within the same city (province) where it was issued, the
presentment period is 10 days from the date of issuance.

# Examples (specification by example — E2E tests are generated from these)
| # | Issuance date | Presentment date | Same city? | Expected |
|---|---|---|---|---|
| 1 | 2026-03-02 | 2026-03-11 | yes | accept |
| 2 | 2026-03-02 | 2026-03-13 | yes | reject: period exceeded |

# Notes / known exceptions
- The legacy system extends the period when it spills over a public holiday (see BR-002) —
  parity: to be preserved.
```

### 2.4 `edge-cases.md` — type-based boundary catalog
```markdown
---
status: approved
---
# Edge-Case Catalog — cheque-management

## Money (Amount)
| Anchor | Boundary | Expected behavior | Rule |
|---|---|---|---|
| Money.negative | amount < 0 | reject (validation) | BR-007 |
| Money.cents | more than 2 decimal places | reject | BR-007 |
| Money.zero | amount = 0 | reject | BR-007 |

## Date
| Anchor | Boundary | Expected behavior | Rule |
|---|---|---|---|
| Date.business-day | period end falls on a weekend | rolls to the next business day | BR-002 |
| Date.year-end | period spans a year boundary | calendar-day counting is unchanged | BR-002 |
```
`test-gen` processes this table as a CHECKLIST: for every type touched by a service in the
manifest, it MUST generate tests for all of the relevant anchors ("if it's on the list",
not "if it comes to mind"). The sector pack (knowledge layer 2) provides the seed of this
file; the project fills it in.

### 2.5 `integrations.md` — behaviors the spec cannot express
```markdown
---
status: approved
---
# core-banking (SOAP, outbound)
- Timeout behavior: on responses over 5s the transaction remains INDETERMINATE → an inquiry
  endpoint is required (evidence: incident INC-4412)
- The amount field expects an INTEGER in kuruş (even though the spec says "amount: number"!)
  (evidence: legacy CHQ_INT.fmt_amount)
```
Purpose: behaviors "not on paper," learned from reverse-engineering and incidents.
Mockifyr stubs are generated consistently with these notes.

---

## 3. Who Writes, Who Reads (skill responsibility matrix)

| Artifact | Writer | Reader | Trigger |
|---|---|---|---|
| context-map | authoring, reverse-engineer | all skills | new BC/service |
| language | authoring (draft) + analyst approval | generate, spec-review, Spec Engine (V8) | new term |
| rules/BR-* | authoring, reverse-engineer (draft) → analyst approval | test-gen, generate, breaker | new/discovered rule |
| edge-cases | sector pack (seed) + reverse-engineer + incidents | **test-gen (mandatory checklist)**, breaker | new type/boundary discovery |
| integrations | reverse-engineer, incident process | generate, Mockifyr stub generation | integration discovery |

DoD tie (foundation 6.2): no skill that touches the domain may open an MR without updating
the relevant file. Spec Engine lint: an approved record without evidence → error; an approved
rule without examples → error; a term used in a spec but missing from language.md → warn.

---

## 4. Locked Decisions (2026-07-03, approved by Ömer)

1. **Location:** `docs/domain/` — part of the living documentation, not Goldpath config. ✔
2. **Rule ID:** `BR-XXX` within a BC; cross-reference as `<bc>/BR-001`. ✔
3. **Language:** Body text in Turkish (the primary user is the analyst) + frontmatter/anchors
   as EN slugs; overridable per customer. (A deliberate inversion of Mockifyr's "repo is EN"
   rule — different target audience.) ✔
