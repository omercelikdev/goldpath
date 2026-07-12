# Goldpath.Bulk — runbooks (bulk RFC §8: runbook or it didn't happen)

Intake verbs live at `/goldpath/admin/bulk`; RUN views (execution progress, repair queue,
replay) live in the JOBS console — a batch executes as a jobs run. The state machine's
rows ARE the audit: every decision carries who/when/why.

## 1. Batch stuck in Received/Validating

- `GET /batches?state=received` — a batch older than a couple of minutes means the
  validate run is not firing: check the jobs console for `GoldpathBulkValidateJob` (paused?
  fleet down?). The cron is the safety net; the upload verb also fires it immediately.
- A batch stuck in `Validating` is a RESUMABLE run mid-flight or after a crash —
  validation is wipe-and-rewrite idempotent; re-firing the validate job redoes it
  honestly. It is a jobs run: takeover and rerun verbs apply there.

## 2. High invalid ratio

- `GET /batches/{id}/errors` — the report names row+field+message, NEVER the value
  (classified data stays out of reports by construction). The raw file is the evidence,
  admin-gated.
- Triage by field: one field failing across many rows = a format/culture mismatch (check
  the definition's `Csv` options); scattered failures = source data quality.
- The uploader fixes the FILE and re-uploads: corrected bytes are a NEW batch by design
  (different hash). The old batch gets rejected with the reason.

## 3. Batch awaiting approval past SLA

- The `goldpath_bulk_awaiting_approval_age_seconds` gauge pages (panel threshold). This is a
  HUMAN queue, not a system fault: find the approver, not the logs.
- `GET /batches?state=validated` lists the queue; `POST /batches/{id}/approve|reject`
  with the authenticated principal — the decision stamps actor + note on the batch row.
- Approve refuses when invalid rows exceed the definition's tolerance — that refusal
  message is the next action (fix the file, or the definition opts into
  `TolerateInvalidRows` as a DESIGN decision, not an incident response).

## 4. Poisoned rows mid-execution

- Row failures land in the JOBS repair queue (`Definition#…` item keys carry
  `batchId#rowNumber`); the batch finishes as `CompletedWithFailures` — a single poisoned
  row never stops the file (scenario-card rule).
- Fix the world (downstream system, reference data), then `replay-items` on the run in
  the jobs console — replay routes through YOUR handler with `Replay = true`; the last
  cleared failure flips the batch to `Completed`.

## 5. Replay after a downstream outage

- Outage mid-batch: failures pile into the repair queue; the batch completes-with-
  failures. When the downstream recovers, `replay-items` drains the queue in one verb.
- Rows the crash interrupted MID-FLIGHT (claimed, never stamped) are ALSO in the repair
  queue marked "interrupted mid-flight" — confirm the downstream state for those before
  replaying (the claim semantics exist precisely so this check is a decision, not a
  surprise double-payment).

## 6. File retention & personal data in raw files

- The raw file is evidence: it lives until the definition's `DeleteFileAfter` elapses
  past the batch's terminal state, then the validate run's purge chunk removes the bytes
  (the batch row and the value-free report SURVIVE for the record).
- An erasure request against a still-retained raw file: the file is a single blob, not
  field-addressable — if the retention window is the problem, shorten `DeleteFileAfter`;
  long-term evidence belongs in the archival module (instructions, not files).
