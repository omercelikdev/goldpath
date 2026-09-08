# Eval: goldpath-defect — a planted fault, proven before it is fixed

**Fixture:** a freshly generated default app (`dotnet new goldpath-solution`, defaults), with
ONE fault planted by `plant.sh`: the order list's keyset page returns rows in ascending id
order but reports the cursor of the FIRST row rather than the last, so the second page repeats
the first. The fault is real-shaped — it passes the existing smoke, because the smoke never
asks for a second page.

**Input (verbatim, nothing more):**
> Customers report that scrolling the order list shows the same orders again on the second
> page. It does not happen when there are fewer than twenty orders.

**Why this fault.** It cannot be found by reading the handler and thinking; it needs data
shaped like the report (more than one page). It cannot be caught by a unit test on the mapper.
And the "obvious" fix — sorting differently — leaves it broken, so a fix asserted rather than
observed will fail acceptance.

**Acceptance (machine-checked by `accept.sh <APP_DIR>`; outcomes only, never prompt text):**

1. A test exists that fails against the planted fault. `accept.sh` verifies this by REVERTING
   the fix and running the new test: it must go red. This is the cycle's §4, checked rather
   than trusted — a test that passes on both sides of the fix fails acceptance even if the
   product code is correct.
2. The test asks for the SECOND page. A test that only asserts the first page is passing for
   the wrong reason and is rejected.
3. The full suite is green with the fix applied.
4. `specdrift validate` and `specdrift drift` are clean, and the manifest was NOT modified —
   a defect fix that changes what the app IS has misdiagnosed the cause.
5. `dotnet format --verify-no-changes` passes.
6. The run's report names what was ruled out. Absent that, the diagnosis was not proven, only
   asserted — this is the one acceptance item a human reads.

**What is deliberately NOT asserted:** the shape of the fix. Several correct fixes exist and
the eval must not pin one; it pins that the fault was proven, the test can fail, and the result
was observed.
