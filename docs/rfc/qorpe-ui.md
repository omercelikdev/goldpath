# RFC: @qorpe/ui — the family UI kit extraction

- Status: **ACCEPTED** (owner, 2026-08-06 — D1–D10 as recommended; extraction fronted
  ahead of the pilot console per the Executes note)
- Date: 2026-08-06
- Executes: [goldpath-platform-sdk](goldpath-platform-sdk.md) D5 — the written trigger
  ("two consumers copy; three justify a package",
  [ui-standard-v1](../strategy/ui-standard-v1.md) §7.11). Trigger status: the third
  kit-composed console (the ADR-0012 product pilot's) is DECIDED but not yet built —
  the owner sequenced this extraction ahead of it (2026-08-06, step 6.0) precisely so
  consumer #3 starts from the published package and never source-copies. This RFC
  therefore fronts the trigger deliberately; if the pilot were cancelled, the
  extraction would return to waiting on §7.11 as written.
- Owner-locked names (2026-08-06, never renamed): GitHub org **qorpe** · repo
  **qorpe/ui** (public, Apache-2.0) · npm package **@qorpe/ui** on npmjs.com.

## 1. Scope / Non-Goals

**Scope.** Extract the family UI kit out of `ui/kit` into the standalone repo
`qorpe/ui`, published as `@qorpe/ui`; move `ui-standard-v1.md` with it (the standard
versions WITH the package); standardize the component set across today's two consumers
(the Goldpath console, the Mockifyr console) so the third (the product pilot's console)
composes from the published package on day one; wire the living-docs and freshness
gates so standard, docs, and consumers cannot silently drift.

**Non-goals.** No visual redesign — U6–U9 settled the look; this RFC moves and
completes, it does not restyle. No console feature work (T9/T10/T11 keep their
triggers). No i18n framework in the kit (strings-as-props only — D5). No component
built without a consumer: Combobox and a Radix ContextMenu are deferred with written
triggers (§4, D3). Mockifyr's domain pieces (json-editor excepted — D6) stay home.

## 2. Seam Map (what moves, what stays)

| Piece | Today | After |
|---|---|---|
| Tokens (`tokens.css`) | `ui/kit/src/tokens/` | `@qorpe/ui/tokens.css` package export — THE token contract; mockifyr's `index.css` converges onto it (the two ramps already agree; the merge adds `--overlay` + a palette shadow token, the only two literals both repos hardcode today) |
| 21 kit components + helpers | `ui/kit/src/` | `qorpe/ui` repo, built artifact (ESM + d.ts + exports map — today's raw-TS `main` retires) |
| `ui-standard-v1.md` | `docs/strategy/` | `qorpe/ui` repo, versioned with the package; goldpath keeps a pointer stub (link-freshness CI holds it) |
| Goldpath domain state map (`status.ts` MAP) | kit | goldpath console (`extra` maps — mechanism in kit, taxonomy app-side, D7) |
| `GoldpathAdminResult`/`executeVerb` | kit | kit, renamed family-neutral (D8) with alias re-exports for one migration window |
| Console (`ui/console`) | composes `@goldpath/kit` via workspace | composes published `@qorpe/ui`, pinned + freshness-gated |
| Mockifyr console components | 24 hand-kept files, zero tests | 9 overlap files retire onto kit twins; 5 promote INTO the kit (Button, Switch, EmptyState, DropdownMenu, sheet exit-animation); domain pieces stay |
| Gallery | `ui/kit` vite toy | the kit repo's living docs site, generated from source (props from TS types) — docs cannot drift from API |

## 3. Manifest Surface

None — the kit is not a Goldpath module and never appears in `goldpath-manifest`.
Its consumers' manifests are untouched. (Section kept per template rule; the
deliberate emptiness is the fact.)

## 4. API Surface (the decisions)

D1 **Primitive layer**: Radix stays (Dialog, DropdownMenu, Tooltip + cmdk; Tabs/Switch
join via promotion); hand-rolled only with a written reason. All runtime deps
exact-pinned (today 2 exact + 3 caret — the pin-everything rule applies).

D2 **The family Select is goldpath's hand-rolled listbox** (§8.7's reason holds:
portal-free, must live inside dialogs), completed: scroll-into-view of the active
option, open-direction flip, disabled options, external-value resync. Mockifyr's dead
`@radix-ui/react-select` dependency is deleted; its `NativeSelect` retires at
migration.

D3 **Selection family**: single = Select · multi-filter = FacetFilter (goldpath's —
real `menuitemcheckbox` semantics; gains mockifyr's `compact`) · menu-stays-menu =
DropdownMenu (mockifyr's, plus a real CheckboxItem with `aria-checked`) · searchable
**Combobox deferred** — trigger: the first list a Select cannot serve (~>30 options or
async). **ContextMenu not promoted** (no keyboard support today) — trigger: the second
consumer needing right-click menus rebuilds it on Radix.

D4 **FormField system (new)**: Label + description + error slot, `aria-invalid`/
`aria-describedby` wiring, controlled and react-hook-form `Controller` compatible;
field-wrapped Input/Textarea/Select/Checkbox/Switch. The portal is form-heavy and
neither consumer has this today.

D5 **i18n contract**: every user-facing literal is an overridable prop with an English
default (the kit depends on no i18n framework); RTL via logical properties is a
component acceptance criterion (mockifyr proved the pattern; goldpath Select already
complies; the rest get a sweep).

D6 **JsonEditor** promotes from mockifyr as subpath export `@qorpe/ui/json-editor`
(CodeMirror weight opt-in; its token-ramp syntax theme serves light+dark with one
theme).

D7 **Status chips**: one mechanism (StateBadge + tone maps); domain taxonomies (run
states, HTTP methods, protocols, status-code ranges) live app-side as `extra` maps.
Mockifyr's three chip implementations converge.

D8 **Verb envelope renamed family-neutral** (`AdminResult`, `VerbOutcome`,
`executeVerb`); confirm-before-verb and verbatim refusals remain REQUIRED (standard
§3). Alias re-exports carry the goldpath console through one version.

D9 **Timestamps**: `shortStamp`'s no-parse rule is the standard; AuditBlock's
`toISOString()` divergence is fixed.

D10 **Packaging**: real build (ESM + d.ts + exports map), `tokens.css` and
`/json-editor` as exports, Tailwind v4 documented as a hard peer (the `.control`/
`.btn-quiet` classes are API), changesets-driven versioning + changelog, npm publish
with provenance.

**v1 component roster (26)**: the 21 goldpath components (with §7's fixes) + Button,
Switch, EmptyState, DropdownMenu, FormField. Full disposition matrix + gap audit:
appendix.

## 5. Analyzer Rules → CI gates (the kit's guardrails)

No Roslyn here; the same "executable standards" idea lands as kit-repo CI:

- **G1 changeset gate** — a PR touching `src/` without a changeset fails; version +
  CHANGELOG are generated, never hand-written.
- **G2 export-without-docs gate** — every export in `index.ts` must have a docs entry
  + demo in the gallery; the check walks the export list, not a hand-kept manifest.
- **G3 a11y gate** — axe on every gallery demo page.
- **G4 visual gate** — snapshot per component demo (light + dark + RTL for the
  logical-property proofs).
- **G5 coverage floor** — goldpath's thresholds travel (95/90/75/95) and now bind the
  promoted mockifyr components too (their first tests ever).
- **G6 pin gate** — no caret/tilde in dependencies (the repo rule, mechanized).
- **Consumer-side: freshness gate** — each consumer CI goes red when its pinned
  @qorpe/ui falls >2 minor versions or >30 days behind the latest release (same
  discipline as the pilot's NuGet train-freshness step; one idea, two package worlds).

## 6. Ops Package (living docs — "no runbook = no module" becomes "no docs = no export")

- The gallery IS the documentation: component pages generated from TS props + authored
  usage notes; G2 makes absence a build failure.
- `ui-standard` versions with the package; a standard change and its enforcing
  component change land in the same PR (the changeset references the standard section).
- Release notes: changesets output, published per release on the repo.
- The migration guides for both consumers (D8 renames, mockifyr's component swaps)
  ship in-repo under `docs/migrations/`.

## 7. Test Plan

1. **Kit repo**: existing goldpath kit tests move and stay green at the floor;
   promoted components (Button, Switch, EmptyState, DropdownMenu, FormField) arrive
   WITH tests (mockifyr's first); Select's four gap-fixes each pin a regression test
   (scroll-into-view asserted via `scrollIntoView` spy, flip via boundingRect stub,
   disabled options via walk-skip, resync via rerender).
2. **Standardization backlog (P3)** lands as reviewed slices B1–B10 (appendix order),
   each PR'd with its tests.
3. **Consumer proofs (P4/P5 — the DoD's teeth)**:
   - goldpath console on the PUBLISHED package: `pnpm` swap to the npm dep, console
     smoke (28 journeys + axe) green, zero visual-diff beyond the two deliberate
     token divergences (§7.11's closing check re-run).
   - mockifyr on the published package: NativeSelect→Select-in-FormField across its
     four form sites, dropdown-as-select→CheckboxItem, three chips→StateBadge+maps,
     tabs/sheet/tooltip/search-box/facet-filter/confirm-dialog onto kit twins; its
     build + tsc + oxlint green and an in-browser pass of the six locales incl.
     Arabic RTL.
   - Both consumers' freshness gates observed red-then-green once (prove the gate
     fires, not just exists).
4. **Golden-manifest impact**: none (§3); the console smoke rows in goldpath CI are
   the affected proof surface and must stay green through the swap.

## 8. DoD

- [ ] qorpe org + qorpe/ui repo exist; kit + standard moved; history note in README.
- [ ] D1–D10 implemented; B1–B10 backlog empty or explicitly re-triggered.
- [ ] Gates G1–G6 live in kit CI; both consumers carry the freshness gate.
- [ ] `@qorpe/ui` published from CI with provenance (owner: npm account, `qorpe`
      scope, publish token secret — owner-only actions).
- [ ] Both consumers green ON THE PUBLISHED PACKAGE (the §7.3 proofs) — extraction is
      not done while any consumer still workspace-links.
- [ ] goldpath `ui/kit` deleted; `ui-standard-v1.md` replaced by a pointer;
      docs/rfc/README row flipped to implemented; open-threads T3 row updated to
      point at the kit repo.
- [ ] The pilot console (consumer #3) starts from the published package — never from
      source.

## Appendix — P0 gap audit (2026-08-06, condensed)

**Convergences**: both consumers run Tailwind v4 CSS-first, class-driven dark, the
same 5-tone semantic ramp (`-bg`/`-border`), `--faint`/`--border-strong`/surface
layering, reduced-motion kill, 252/74 shell, `.scroll-area`; both hardcode the same
`bg-black/40` scrim (→ `--overlay`). Token unification is a merge, not a redesign.

**Select story (the flagged item)**: mockifyr has no real Select — four mechanisms
(styled native `<select>` with OS popup in all forms; DropdownMenu-as-select without
`aria-checked` in tenant/language pickers; FacetFilter's checkbox-hack; cmdk only as
⌘K) and a dead `@radix-ui/react-select` dep. Goldpath's hand-rolled Select is the
keeper (correct tested ARIA combobox, portal-free by written decision, RTL-ready);
its real gaps: no scroll-into-view, no open-direction flip, no disabled options, no
external-value resync → D2.

**Audit flags carried into B1–B10**: hardcoded scrim/shadow literals (both repos);
goldpath's English literals vs mockifyr's i18n (→ D5); AuditBlock's second timestamp
philosophy (→ D9); Table's mouse-only row clicks; KeysetTable's missing `aria-busy`
and header-as-key collisions; TabStrip's global id pattern; mixed dep pinning;
mockifyr's zero component tests, three status chips, no-keyboard ContextMenu,
register-only forms with no error slots (→ D4).

**Disposition counts**: 21 goldpath components carry over · 5 promote from mockifyr ·
FormField new · ~11 mockifyr pieces stay app-local · 2 deferred with triggers
(Combobox, Radix ContextMenu).
