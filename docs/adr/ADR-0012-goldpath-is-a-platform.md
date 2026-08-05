# ADR-0012: Goldpath is a platform — product modules ride the published core

- Status: **proposed** (2026-08-05) · Composes with ADR-0001 (manifest truth), ADR-0003
  (configure-not-wrap), ADR-0008 (GM gate); amends foundation §10's commercial line

## Decision
Goldpath is a PLATFORM, not only an accelerator: first-party PRODUCT modules (Sync, API
Portal, …) are built ON it as a new shelf beside Ring C — **products** — and they bind to
the platform exactly the way an adopter does: **published `Goldpath.*` packages plus a
real `Goldpath.Sdk` package, never source** (foundation §10 — the `packages/shared`
compile-link seam is an internal privilege that does not leave the monorepo). A product
module stands up STANDALONE (its own solution, its own deployment shape — clean or
vertical, chosen like any generated app), declares itself in the manifest under a
NAMESPACED product key (`qorpe.sync` — the core `features` enumeration stays closed),
and wears the ONE console family: its console is its own app composed from the kit,
joining operators' view through the cross-service registry — never a runtime plugin
(§5.0's toggle rule stands: composition is compile-time, Program.cs stays the honest
inventory). The core stays open (Apache-2.0); product modules MAY be closed and
privately distributed — open core, explicitly.

## Rationale
The platform claim was implicit everywhere (the SDK-shaped shared seam, the console's
"different audience becomes its own group" comment, the registry that already federates
services) and written nowhere — so every product-module question (how does it declare
itself? version itself? ship a console?) re-litigated the architecture. Binding products
through the published train keeps one discipline for adopters and ourselves: a product
module is the platform's permanent first adopter, and every seam it needs is a seam
customers get too.

## Consequences
- `Goldpath.Sdk` becomes the contract for out-of-repo modules (platform-sdk RFC D1);
  versioning: core packages keep the D1 lockstep train; product modules PIN a train like
  adopters (CorPay pattern) and are supported per the same D3 window.
- The manifest schema gains a namespaced `products` surface (RFC D2) with corpus rows
  and a golden-manifest shape before any product ships (ADR-0008).
- Foundation §10's "included in project fee" line is amended: the core's positioning is
  unchanged, product modules may carry their own commercial terms.
- The one-family promise is enforceable: a product console composes `@qorpe/ui` (the
  extracted kit — RFC D5) and passes the same ui-standard conformance bar.
