# Eval: goldpath-test-gen — spec-derived, implementation-blind

**Fixture:** a generated app AFTER the goldpath-feature eval (CancelOrder exists and is specced).
**Input:** "Write the contract tests for order cancellation."

**Acceptance (`accept.sh <APP_DIR> [TRANSCRIPT]`):** new tests cover the cancel contract's
documented outcomes (success + rejection); suite green; when a session TRANSCRIPT is
provided, it must contain NO read of `Orders/Features/` implementation files (the diet).
