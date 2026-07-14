# UI standard v1 — the Goldpath console's visual contract

Status: ADOPTED for the stack-agnostic VISUAL layer — tokens, typography, status
language, interaction rules (2026-07-14). The delivery mechanics (Tailwind mapping,
npm packaging) ride the console RFC's D1 and become binding with its acceptance. Lineage: extracted from the
Mockifyr dashboard's token system, which itself mirrors the Praxis design system — one
visual family across the product line. The near-black ("siyah") accent logic is the
identity; re-skinning a tenant is a ONE-FILE change.

## 1. Tokens (the single source of truth)

Lifted verbatim from Mockifyr `ui/src/index.css` — the kit vendors the same structure:

- **Layered neutrals**, light: `--app #ffffff` (frame) → `--surface #f5f5f7` (body) →
  `--background #ffffff` (cards) → `--muted #ececef` → borders `#e6e6e9/#d7d7db`.
  Text: `--foreground #18181b`, `--muted-foreground #71717a`, `--faint #a1a1aa`.
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
| warning | Running+predicted-overrun | Validated (awaiting gate) | Suppressed | PendingApproval |
| danger | Failed | CompletedWithFailures/Rejected | Failed | Rejected/Failed |
| violet | Recovering/Resumed | — | — | — (reserved: replay/repair flows) |

## 6. What the standard is NOT

No custom DSL over the CSS layer (Tailwind per the console RFC's D1), no per-screen color invention, no accent-colored status,
no webfonts, no page-owned scrolling. A screen that needs a token that does not exist
is a design conversation, not a hex code in a component.
