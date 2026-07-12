# ADR-0001: The manifest is the single source of truth

- Status: accepted (2026-07-03)

## Decision
`.goldpath/manifest.yaml` (validated against JSON Schema, `schemas/manifest/`) is the single
source of truth for a solution/service. The wizard, CLI, skills, templates, and CI read from
and write to the manifest. CLI flags are merely one way of producing a manifest.

## Rationale
Multiple configuration sources (flags, config, convention, docs) diverge within three months;
AI agents must have a single trustworthy point of reading. Manifest ↔ code consistency is
enforced in CI via Spec Engine `validate/drift` (toggle semantics: composition is compile-time).

## Consequences
- A module that is not in the manifest does NOT exist in the application (at the csproj/Program.cs/DLL level).
- The manifest is environment-agnostic; environments/promotion are the job of CI and org-config.
- The schema is SemVer'd; Spec Engine declares its compatibility range.
