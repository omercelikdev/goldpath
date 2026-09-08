---
name: goldpath-defect
description: Run a DEFECT through the delivery cycle — prove the cause with evidence before touching code, write the failing test and prove it fails, fix, then verify for real. Use when something is broken, misbehaving, or reported as a bug; for new behaviour use goldpath-feature instead.
---

# goldpath-defect — a fault, proven before it is fixed

You are fixing something that is already wrong. The expensive mistake here is not a bad fix —
it is fixing the wrong thing confidently, because the first plausible explanation was taken as
the cause.

This skill enforces the **sequence** in `.claude/cycle.md`. It carries no rules of its own; the
rules live in `cycle.md`, `.claude/conventions.md` and the manifest.

## The gates, in order

Do not pass a gate by asserting it. Each one is a thing you RAN.

**1 — The cause, with evidence**
Not a hypothesis. Read the logs. Query the data and size it: how many rows, since when, which
tenant. Open what the report attached — a report that names a screen, a page size or an id is
telling you where to look.

If two explanations fit, **rule one out in writing** before building on the other. Write which
one you eliminated and how. That sentence is worth more than the fix.

**2 — Agreement**
State the diagnosis and wait, unless the fault is trivial and self-evident. A wrong diagnosis
agreed early costs a conversation; a wrong diagnosis discovered late costs the change.

**3 — The failing test, and proof that it fails**
Write the test that fails for the reason in the report, and watch it fail. Then **put the fault
back and confirm it goes red again.** A test green on both sides of the fix proves nothing and
advertises that it does.

Pick the layer that can actually fail — `cycle.md` §5. A database-decided fault has no unit
test that can catch it.

**4 — The smallest fix**
Only what makes the test pass. A defect is the worst possible moment to refactor: it hides the
fix inside noise and it makes the revert expensive.

**5 — The engine**
`spec_validate` and `spec_drift`, both clean. If the contract changed, the committed copy in
`specs/` changes with it.

**6 — Run it for real**
Reproduce the ORIGINAL report against the fix, the way it was reported. If there is a screen,
open it and use it; sign in if it asks. Measure — the row, the payload, the computed value. A
fix that was never observed working is a hypothesis with a green test attached.

**7 — The blast radius**
What else used the path you changed? Does an existing test encode the behaviour you just
corrected — and is that test now asserting the bug? Move it deliberately and say so.

**8 — Evidence**
The merge request carries the cause with its proof, what you ruled out, the test that would
have caught it, and what you saw when you reproduced it.

## Never

- Close a defect you could not reproduce. Say so, record what was ruled out, leave it open.
- Fix a symptom you can see while the cause stays unproven — say that is what you are doing,
  and why, in the merge request.
- Delete or weaken a test to make the build green.
