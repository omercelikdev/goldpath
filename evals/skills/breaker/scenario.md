# Eval: breaker — falsification as a deliverable

**Fixture:** a generated app with at least one non-trivial feature (post goldpath-feature eval).
**Input:** "Break the order lifecycle."

**Acceptance (`accept.sh <APP_DIR>`):** `Breaker_`-marked executable scenarios exist in
tests/; `tests/BREAKER-VERDICT.md` exists and names targets attacked + the verdict; the
suite runs (kept scenarios green OR failures reported with their spec sentence in the verdict).
