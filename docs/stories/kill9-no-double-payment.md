# Proof story: kill -9 mid-batch, and why no payment goes out twice

The scenario every payments architect asks about first: a node executing a batch of
corporate payments dies — power, OOM-kill, a bad deploy — mid-chunk. What state is the
money in?

## The setup (a real test, not a whiteboard)

`BulkClusterTests.Kill9_midbatch_recovers_on_the_other_executor_without_a_double_payment`
runs in CI on every pull request. It is not a simulation: two REAL executor processes
join one clustered store over real PostgreSQL; a 30-row payment batch is uploaded,
validated, four-eyes approved and fired through the cluster verb; the test watches the
sink until the winning executor is visibly paying — then `Process.Kill(entireProcessTree)`
takes it down mid-chunk. (Detail that mattered: EITHER node can win the fire, so the
test identifies the winner from the sink's process id and kills THAT one — killing
blind proved nothing and flaked.)

## What the machinery does

1. **The claim lands before any side effect.** Every row in a chunk is marked CLAIMED
   in the database before its handler runs. A row that is claimed but never stamped
   `Executed` is, by definition, "interrupted mid-flight".
2. **Quartz clustering detects the corpse.** Missed check-ins mark the dead scheduler;
   the fire re-fires on the surviving node with recovery semantics.
3. **The run RESUMES, never restarts.** The runner finds the open run, resets the dead
   node's stale claims, and continues from the last checkpoint — completed chunks are
   never re-executed.
4. **Interrupted rows go to the REPAIR QUEUE, not back out the door.** The resumed
   chunk does not re-send a claimed-but-unstamped row; it files it as a repair item
   with a teaching message: *confirm the downstream state, then replay*. Whether the
   bank actually processed that one instruction is a fact only the bank knows — so a
   HUMAN confirms, and `replay-items` heals it through the same audited verb.

## The assertions that make it a proof

- **On the automatic path — kill, takeover, resume — no sink row number appears
  twice.** The machinery NEVER re-sends on its own; that is the guarantee.
- Both executors actually paid rows (the recovery genuinely happened).
- The books balance at the terminal state: `Executed + Failed == ValidRows`.
- After replay, the batch is `Completed` and every row number is accounted for exactly
  once in the ledger. The replay itself is different in kind: a HUMAN authorized a
  re-send after confirming downstream state — so the one interrupted row may
  legitimately produce a second sink entry, and the test allows exactly that. The
  system's promise is precise: it never duplicates a payment BY ITSELF; a deliberate,
  audited human decision can.

## What the proof itself caught

Running this on CI hardware (slower, wider race windows) exposed a real ledger bug the
fast dev machine had hidden: a kill landing between a chunk's row-stamp write and its
counter increment left the batch counters one short — money right, books wrong. The
fix makes the terminal flip recount counters from the row stamps (the rows are the
truth; the counters are its cache). The test that found it now guards it, every PR.

That is the pattern this repository runs on: the claim is a sentence, the proof is a
test, and when they disagree the proof wins.
