# FileExchange — Ops Runbook

## "The counterparty says they sent it" triage
1. `GoldpathFileReceived` fired? If not, the file never reached the rail — check the
   pick-up job's last run in the Jobs console before suspecting the engine.
2. `GoldpathFileRejected` instead? The file failed its FILE-level contract (truncation,
   trailer mismatch) and ingested NOTHING — the reason is on the event and in the log.
   Ask the counterparty to resend; do not hand-edit the file.

## Quarantine depth and age
- Quarantined rows carry their reason and 1-based line number (what the operator sees in
  the file). A rising quarantine count on one rail is a counterparty format drift —
  compare reasons before opening N tickets for one cause.
- Reprocessing is safe BY CONSTRUCTION: run the file again — applied rows dedup on the
  `(rail, file, line)` key, fixed rows apply and their quarantine records clear. Zero
  duplicates is the tested invariant, not an aspiration.

## Duplicate-file storms
`SkippedAsDuplicate == row count` means the transport re-delivered a file already
ingested. That is the rail working as designed — alarm only if the SOURCE generates
distinct files with identical names (then the file naming, not the rail, is the bug).

## Missed arrival windows
The rail engine owns one run; SCHEDULES are Jobs-module business. Alarm on the pick-up
job's deadline, not on the absence of events — an empty day and a dead schedule look the
same from the event stream alone.

## The console panel and the admin surface
`/goldpath/admin/fileexchange` (read-only): `/rails` says which rail is holding rows, `/files`
which file, `/quarantine` which line and WHY — in the engine's own words (`parse: …`,
`handle: …`, or the row-contract reason). The console's "File rails" panel and its Today
card ("Rows in quarantine") read exactly these. Triage from the reason, not the count: ten
rows with one reason is one counterparty conversation.

## Dashboard
`grafana-fileexchange-dashboard.json` — files received vs rejected, rows applied vs
quarantined vs skipped-as-duplicate, per rail (`Goldpath.FileExchange` meter). A rising
duplicate line is the transport re-delivering (fine); a rising quarantine line on one rail
is format drift (call the counterparty); a rejected file is a whole-file contract failure
(ask for a resend, never hand-edit).

## Ledger
The in-memory ledger loses state on restart — tests and single-node demos only. Compose a
database-backed `IGoldpathFileLedger` before production; the idempotency guarantee is
only as durable as the ledger under it.
