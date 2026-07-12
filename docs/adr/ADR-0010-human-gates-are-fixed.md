# ADR-0010: Human gates are fixed

- Status: accepted (2026-07-03)

## Decision
Three gates can never be bypassed by any skill/automation: **spec approval** (business),
**MR approval** (developer), **cutover approval** (ops). Every agent output arrives as an MR.
In migrations, parity contract deviations (approved deviations) are business-approved;
the differential diff triage decision belongs to a human.

## Rationale
The banking/telco audit reality: the answer to "who approved this" must always be a human
and a git trail. The "full agentic" promise is neither realistic nor defensible in an
audit — the right promise: an agent performs every phase, a human passes every gate.

## Consequences
- Gate bypassing is also technically closed off (branch protection + pipeline rules).
- Traceability chain: test ← spec example ← business request (end to end in git history).
