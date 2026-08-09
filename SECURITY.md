# Security policy

## Reporting a vulnerability

**Do not open a public issue for a security problem.** Use GitHub's private reporting —
[Security → Report a vulnerability](https://github.com/omercelikdev/goldpath/security/advisories/new)
— which opens a private advisory only the maintainer can see.

If that path is unavailable to you, write to **omer@omercelik.dev** with `GOLDPATH-SECURITY`
in the subject.

What to include: the package and version, what an attacker gains, and the smallest
reproduction you have. A manifest plus a failing request usually beats a paragraph.

What to expect:

| Stage | Target |
|---|---|
| Acknowledgement that a human has read it | 3 working days |
| First assessment (is it real, how severe, does it affect a published train) | 10 working days |
| Fix or a written mitigation for a confirmed high/critical | 30 days |
| Coordinated disclosure | with you, after the fix ships — credit given unless you decline |

This is a coordinated-disclosure policy: please give the fix a chance to reach adopters
before publishing details.

## What is in scope

The published `Goldpath.*` packages, the `Goldpath.Cli` tool, the solution/worker templates,
and this repository's build and release pipeline. Findings in a dependency belong upstream —
tell us anyway if a Goldpath default makes it reachable, because the default is ours.

Out of scope: an adopter's own application code, and the infrastructure an adopter runs
(their broker, database, gateway or cluster).

## Supported versions

Goldpath is at `0.1.0-preview.6`. **While the version is below 1.0, only the newest preview
train receives fixes** — the versioning contract calls this out
([docs/rfc/goldpath-versioning.md](docs/rfc/goldpath-versioning.md)), and the upgrade notes
in `docs/upgrades/` carry every train-to-train change. At 1.0 the support window becomes the
stated one in that contract, not this line.

An adopter who cannot move trains quickly should say so in an issue: a backport is a
decision we can make, but not one we can guess.

## What ships with each release, so you can verify it yourself

- **An SBOM** (CycloneDX, `goldpath-sbom.cdx.json`, attached to the GitHub release) — the
  resolved dependency graph of the release build, so "are we affected by CVE-X?" is an
  artifact lookup rather than an investigation.
- **Signed build provenance** (SLSA, keyless via GitHub OIDC) for every `.nupkg`. It states
  which commit, which workflow and which run produced the package. Verify with:

  ```bash
  gh attestation verify Goldpath.Data.0.1.0-preview.6.nupkg --repo omercelikdev/goldpath
  ```

  A package that fails this check did not come from this pipeline, whatever the registry
  says.
- **Publishing without a long-lived key**: the train is pushed through NuGet Trusted
  Publishing (OIDC), so there is no API key in this repository to leak. The same is true of
  the family UI kit on npm.

## Hardening notes for adopters

- The admin surface (`/goldpath/admin/*`) is **fail-closed** by default: with no auth
  configured it refuses rather than serves ([docs/rfc/goldpath-admin-contract.md](docs/rfc/goldpath-admin-contract.md)).
- Classified fields are masked at the boundary, and the console masks them again on render —
  defence in depth, deliberately duplicated.
- Air-gapped installation is a first-class scenario: nothing in a generated app phones home,
  and every dependency is pinned. There is no runtime licence check anywhere in Goldpath.
