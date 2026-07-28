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
   **GROUPED nav with small-caps group labels** — the console groups by concern:
   `OVERVIEW` (Today) · `EXECUTION` (Runs) · `INTAKE` (Bulk) · `OUTBOUND`
   (Campaigns, Notifications) · `COMPLIANCE` (Archival) — future modules land in a
   group instead of growing a flat list. Active item = soft fill + **2px left border**
   in the accent. Footer: tenant/service switcher card + preferences row.
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
   aggregate).
8. **Page headers**: every screen opens with title + one-line purpose sentence
   (Mockifyr's "Here's what's happening…" pattern); banner-ish summary strips become
   header cards.
9. **Cross-screen context**: rows that reference another screen's entity LINK to it
   (a run's job name → Jobs; a triage row already deep-links — that becomes the norm),
   so the relationships read on the screen instead of in the docs.
10. **Search**: global ⌘K over nav + entity ids; per-table search only where a list
    is take-bounded (it narrows SERVER-side via the existing filters, never a loaded
    page).
11. **Family conformance**: tokens/type/radii re-checked against the reference each
    U7 batch; divergence is a defect. Extraction of the family into a shared package
    (the `@qorpe/ui` question) is DEFERRED with a written trigger: the third consumer.
    Two consumers copy; three justify a package.

Verification unchanged in kind: axe + console smoke green through every batch, and the
row-click/Sheet change updates the smoke's locators in the same PR.
