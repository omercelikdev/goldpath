# Upgrade guides

One file per release, named with the FULL train version (`<version>.md`, e.g.
`0.2.0.md`, `0.2.1.md`, `0.1.0-preview.md`), created by the release PR — the D4 gate
in `docs/rfc/goldpath-versioning.md` makes it mandatory for every release, patches
included (a patch file is normally the single no-breaking-changes line).

Format per file:

- **From / to** versions and the date.
- One section per breaking change, each with: what changed (the PublicAPI/contract
  diff), why, and the exact steps to take (code edit, `goldpath db add <Name>` for
  `[schema]` changes, config rename).
- When the release breaks nothing, the whole file is the single line:
  "No breaking changes — take it blind."

The first file appears with the first tagged release (0.1.0-preview, issue #10).
