# Moving the repository to the `qorpe` organisation

The package ids (`Goldpath.*`), the namespaces, the CLI verb, the diagnostic ids and the
admin route prefixes do NOT change. Only the GitHub owner does. This file exists because
two of the steps are browser-only and one of them fails a release LATE and confusingly if
it is forgotten.

## What actually breaks (as opposed to redirecting)

GitHub keeps a redirect from the old slug indefinitely, so every issue link, badge, blob
link and `git fetch` keeps working after the move. Three things do not:

1. **nuget.org trusted publishing.** The policy is bound to `omercelikdev/goldpath` on
   nuget.org's side. After the move the release workflow's OIDC token carries
   `qorpe/goldpath` and the exchange is refused — at the `trusted-publishing login` step,
   which runs AFTER pack, SBOM and provenance have all gone green. It looks like a late,
   mysterious failure. Re-point (or re-create) the policy in the nuget.org UI before the
   next tag.
2. **Build provenance on packages already published.** Their attestations name the old
   repository. `SECURITY.md` says so; the verify command is era-dependent by nature.
3. **The redirect itself, if anyone ever creates a new repo at the old slug.** Do not.

## Order

**Before**
- Land any in-flight release. Never move between a tag push and a successful publish.
- Confirm whether packages keep publishing under the `omercelikdev` NUGET profile. That
  profile name is `release.yml`'s `user:` field and is NOT the GitHub owner — it changes
  only if the nuget account changes.

**Browser, account owner**
- Transfer the repository (Settings → Danger Zone), admin on both sides.
- nuget.org → Trusted Publishing: re-point every `Goldpath.*` policy to `qorpe/goldpath`.
- Secrets: there is nothing to preserve. `ANTHROPIC_API_KEY` is deliberately UNSET (T4,
  owner decision on cost), and `GITHUB_TOKEN` is provided by Actions. Worth knowing for
  the day it IS set: the review-agent workflow skips silently without it and the job
  still goes green, so a lost key would stop reviews without turning anything red.
- Confirm the org allows the third-party actions this repo uses (`pnpm/action-setup`,
  `anchore/sbom-action`, `NuGet/login`) and permits `id-token: write` and
  `attestations: write`.
- Re-check branch protection on `main` and that the required checks still map to the jobs.

**Local**
- `git remote set-url origin https://github.com/qorpe/goldpath.git`. The `goldpath-*`
  worktrees share the parent's config, so one command covers them all. This matters
  beyond convenience: `scripts/ledger-check.sh` resolves issues through `gh`, and against
  a stale remote it reports MISSING, which it SKIPS — the gate would pass vacuously.

**After (this pull request)**
- Seventeen substitutions of `omercelikdev/goldpath` → `qorpe/goldpath` across eight
  files. Never substitute the bare owner: `specdrift`, `mediant` and `mockifyr` links and
  the nuget profile name in `release.yml` all contain it and none of them moves.

**Verify**
- `grep -rIn 'omercelikdev/goldpath'` returns only this file and `SECURITY.md`'s
  attestation note. Both mention the old slug ON PURPOSE — one describes the move, the
  other tells a verifier which repository signed the packages published before it. A
  greedy substitution would have broken the security instruction it was meant to fix.
- `./scripts/ledger-check.sh` still resolves issue states.
- On the next release, watch the `trusted-publishing login` step. It is the canary.
