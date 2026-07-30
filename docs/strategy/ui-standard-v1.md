# UI standard v1 — the Goldpath console's visual contract

Status: ADOPTED for the stack-agnostic VISUAL layer — tokens, typography, status
language, interaction rules (2026-07-14). The delivery mechanics (Tailwind mapping,
npm packaging) ride the console RFC's D1 — accepted 2026-07-14, so binding. Lineage: extracted from the
Mockifyr dashboard's token system, which itself mirrors the Praxis design system — one
visual family across the product line. The near-black ("siyah") accent logic is the
identity; re-skinning a tenant is a ONE-FILE change.

## 1. Tokens (the single source of truth)

Lifted verbatim from Mockifyr `ui/src/index.css` — the kit vendors the same structure:

- **Layered neutrals**, light: `--app #ffffff` (frame) → `--surface #f5f5f7` (body) →
  `--background #ffffff` (cards) → `--muted #ececef` → borders `#e6e6e9/#d7d7db`.
  Text: `--foreground #18181b`, `--muted-foreground #65656c`, `--faint #6a6a72`.
  Both secondary inks clear WCAG AA (4.5:1) against the SURFACE, not just against white —
  the axe gate in the console smoke is what says so, and it is what corrected the original
  pair (4.44 and 2.56) on 2026-07-27.
- **Accent = near-black**: `--primary #18181b` on light, `#fafafa` on dark — actions,
  active states, CTA. Swap `--primary` to re-skin; NOTHING else changes.
- **Semantic status ramp is deliberately separate from the accent**: success/warning/
  danger/info/violet, each with `-bg` and `-border` companions — badges, pills, state
  chips. The accent never carries meaning; the ramp never carries brand.
- **Dark mode is class-driven** (`.dark` on `<html>`) so a customer can force either;
  the sidebar melts into the frame (same token) instead of reading as its own panel.
- Radii from one `--radius: 0.625rem` (sm/md/lg/xl/2xl derived); one soft
  `--shadow-surface`.

## 2. Typography & density

System font stacks (sans + mono; no webfont downloads), **base 14px**, antialiased.
Dense-but-breathing: the console is an operator tool, not a marketing page — tables are
the primary surface, cards lift on `--background` above `--surface`.

## 3. Shell & interaction rules

- **The app-shell owns scrolling, never the page** (`body { overflow: hidden }`);
  scrollable regions are explicit `.scroll-area`s with auto-hiding scrollbars
  (invisible until hover/focus).
- **Focus**: 2px `--ring` outline for keyboard users on buttons/links; form fields
  carry a subtle border tint instead (the heavy ring reads wrong on inputs).
- **Reduced motion is honored globally** (`prefers-reduced-motion` kills transitions).
- Confirm-before-verb: every mutating admin verb goes through the confirm dialog and
  surfaces the `GoldpathAdminResult` message verbatim — refusals TEACH, the UI never
  paraphrases them.

## 4. The primitive inventory (the kit's contract)

Inherited from Mockifyr's proven set, extended with Goldpath-specific composites:

| From Mockifyr | Goldpath composites (new) |
|---|---|
| app-shell · sidebar · tenant-switcher · command-palette | **keyset table** (cursor pager, `take` clamp aware — never offset/total-count UI) |
| button · badges · tabs · sheet · switch · field | **state badge** (domain states → semantic ramp mapping below) |
| confirm-dialog · dropdown/context menu | **verb button** (POST + `GoldpathAdminResult` envelope + 400-refusal surface + audit hint) |
| search-box · facet-filter · empty-state | **run progress** (chunks, items/s, predicted-finish vs deadline) |
| json-editor · error-boundary · login-gate | **audit trail block** (old→new change rows, masked classified fields) |

## 5. Domain state → status ramp mapping

| Ramp | Run model | Bulk | Notification | Payments (sample) |
|---|---|---|---|---|
| success | Completed | Completed | Sent | Executed |
| info | Running | Executing/Validating | Requested | Submitted |
| warning | Running+predicted-overrun (composite: the badge takes the tone via `StateBadge`'s explicit `tone` override — `extra` cannot, since the standard MAP wins collisions) | Validated (awaiting gate) | Suppressed | PendingApproval |
| danger | Failed | CompletedWithFailures/Rejected | Failed | Rejected/Failed |
| violet | Recovering/Resumed | — | — | — (reserved: replay/repair flows) |

## 6. What the standard is NOT

No custom DSL over the CSS layer (Tailwind per the console RFC's D1), no per-screen color invention, no accent-colored status,
no webfonts, no page-owned scrolling. A screen that needs a token that does not exist
is a design conversation, not a hex code in a component.


## 7. v1.1 — the family alignment (owner feedback batch, 2026-07-29; drives console U7)

The owner reviewed the live console against the Mockifyr dashboard side by side and set
one rule above the items: **everything below becomes a STANDARD** — defined once in the
kit, swept everywhere, no screen updated alone. Extracted from the running Mockifyr
dashboard and its source (not from memory):

1. **Icons: lucide-react**, the family's one set. Sparse by design — nav items, stat
   cards, empty states; never decoration on prose. (Mockifyr already ships it.)
2. **Sidebar**: brand head (mark + product word + subtitle) · **⌘K search** ·
   **GROUPED nav with small-caps group labels** — amended after the owner reviewed B1:
   the five modules are ONE family and share ONE `MODULES` group (the subtitle already says Operations — the same word twice in one rail was the redundancy a test caught) under `OVERVIEW`
   (Today), exactly as the reference keeps its whole core domain in a single group. A
   heading must own several items to earn its place. A surface for a DIFFERENT audience
   (an API portal, platform settings) becomes its own group; a new operations module
   joins the existing one. Active item = soft fill + a short **inset accent bar** at its left edge (a
   highlight, not a border — items carry no borders at all; amended to match the
   reference exactly after the owner reviewed B1). Footer: tenant/service switcher card + preferences row.
3. **Collapse**: icons REMAIN when collapsed (icon-only rail, centered, tooltips),
   state persists (localStorage), and the toggle is a proper icon button — all three
   exactly as the reference behaves.
4. **Tables**: one kit Table pattern — header tone, zebra-less rows with hover, row
   click opens a **right-side Sheet** (drawer) titled with the entity and its
   one-line description; the inline-below detail pattern retires everywhere.
5. **Filters**: selects give way to **search-box + facet-filter** (multi-select chips
   with counts) on every take-bounded list; date windows keep native inputs styled by
   the kit.
6. **Tabs are pills** (`bg-muted` rail, `rounded` triggers) — the underline strip
   retires.
7. **Stat cards** on Today and section overviews: icon + label + number (+ small
   trend where the API already carries the numbers — the console still invents no
   aggregate). Shipped in b4a as the kit `StatCard` (class-verbatim from the
   reference dashboard): Today opens with one card per module the estate can
   COUNT — failed runs, awaiting approval, failed campaign items, failed
   notifications, due to archive — each summed from the same take-bounded lists
   the triage reads (the scope line covers them), toned only when non-zero, and
   deep-linking to the service that owns the number. A module nobody composed, or
   whose surface could not be read, shows NO card rather than a false zero.
   Trends stay deferred with their trigger: the contract publishes no time
   series to draw one from.
8. **Page headers**: every screen opens with title + one-line purpose sentence
   (Mockifyr's "Here's what's happening…" pattern); banner-ish summary strips become
   header cards.
9. **Cross-screen context**: rows that reference another screen's entity LINK to it
   (a run's job name → Jobs; a triage row already deep-links — that becomes the norm),
   so the relationships read on the screen instead of in the docs. Shipped in b4b as
   three links riding an INTENT pattern (a fresh `{ … }` object per ask, so asking
   twice still lands): a history row's job opens the Jobs tab with the job's sheet;
   a trigger's calendar opens the Calendars tab; a batch's run id crosses sections
   into the run console's History with the run's sheet open. Each link lights only
   when the console can actually take you there — a batch's run id on a service
   that composes no jobs surface stays a plain fact.
10. **Search**: global ⌘K over nav + entity ids; per-table search only where a list
    is take-bounded (it narrows SERVER-side via the existing filters, never a loaded
    page). Shipped in b3d as the kit `CommandPalette` (cmdk, reference-verbatim):
    ⌘K/Ctrl-K toggles it, the rail's search trigger opens it, and it offers every
    rail destination plus the estate's service switch. Entity-id search is DEFERRED
    with a written trigger: it needs a cross-module lookup endpoint on the admin
    contract — the console keeps no client-side index to fake one with.
11. **Family conformance**: tokens/type/radii re-checked against the reference each
    U7 batch; divergence is a defect. Extraction of the family into a shared package
    (the `@qorpe/ui` question) is DEFERRED with a written trigger: the third consumer.
    Two consumers copy; three justify a package.

    Closing check (b4c, 2026-07-29): a mechanical token diff against the reference's
    `index.css` shows exactly two divergences, both deliberate and both already
    documented — the reference's `--brand` pair has no Goldpath counterpart (the
    near-black accent IS the identity, §1), and the secondary inks
    (`--faint`/`--muted-foreground`) carry the 2026-07-27 WCAG AA correction the axe
    gate demanded (§1). Radii, shadows, status ramp, dark palette: identical. The
    README's three pictures are re-captured from the finished family UI by
    `scripts/console-screenshots.sh` in the same PR.

Verification unchanged in kind: axe + console smoke green through every batch, and the
row-click/Sheet change updates the smoke's locators in the same PR.


## 8. v1.2 — the finalize batch (owner review of the finished U7 UI, 2026-07-30; drives U8)

The owner walked the live console screen by screen. Same rule as §7: every item is a
STANDARD — fixed once, swept everywhere, nothing patched on a single screen.

1. **Today gets the standard PageHeader** — its small inline heading is the odd one
   out; every screen opens the same way (§7.8), the estate screen included.
2. **The triage list modernizes**: the floating row-cards give way to the family's
   card-list (one bordered card, rows divided inside — the reference dashboard's
   activity-list pattern).
3. **Status/info cards standardize**: the run console's flat fleet block (and any
   sibling flat info block on other screens) becomes the family card — the surface
   the StatCard/Panel already use.
4. **Verbs grow icons**: the universal verbs (pause, resume, trigger, rerun, remove,
   approve, reject, abort) carry their lucide icon with the label a hover away —
   icon-only where the icon is unmistakable, icon+label where it is not. Sparse by
   §7.1: domain verbs (verify, retrieve, erase…) keep their words.
5. **Collapsed rail tooltips**: an icon-only rail item must SAY its name on hover —
   a real tooltip (the reference's), not the browser's title delay.
6. **The fleet switcher restyles**: the black pill retires; choose-one-of-few is the
   pill-tab pattern (§7.6) here too. Any sibling black-pill selector follows.
7. **Selects standardize**: native blue-focus selects retire for the family select
   (field skin + chevron), one kit control, every screen.
8. **Checkboxes standardize**: the native blue checkbox retires for the family box —
   the FacetFilter's drawn box (primary fill + check) becomes the one checkbox.
9. **The keyset footer quiets**: "N loaded · end" loses its elevated strip (border +
   tint); it reads as a quiet line under the table, nothing more.
10. **Text fields standardize**: every free-text input (aggregate key, search, notes,
    dates) wears the ONE field skin — h-9, rounded-lg, the SearchBox's field. And a
    field must not SHIFT its neighbours: the clear icon lives INSIDE the reserved
    width, never appended beside it.
11. **The upload row composes properly**: definition + file + verb laid as a designed
    row of the family's controls, not primitives in a line.
12. **Batches gain search**: the standard SearchBox, far left of the filter row — it
    commits a batch ID and reads THAT batch from the server (the contract has no text
    search to send; an ID lookup is what it does have).
13. **Facets go truly multi**: selecting several values means OR, on every facet of
    every screen. The frozen contract takes ONE value per filter today, so this is a
    contract REVISION (R3, additive: repeated query params) that the server grows
    first — the console keeps refusing to fake OR by merging take-bounded pages.
14. **Campaigns joins the family**: its plain state select becomes the standard facet,
    and its inline-below detail — and ANY remaining inline-below detail — opens in
    the right Sheet (§7.4 finishes).
15. **Sweep rule**: each of the above lands as a kit-level change swept across all
    screens in the same PR — no screen updated alone.
