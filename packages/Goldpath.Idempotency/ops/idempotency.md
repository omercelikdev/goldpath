# Idempotency — Ops Runbook

## "Why did this request answer 409 / 422" triage
1. `409 Conflict` = a request with the same `Idempotency-Key` is IN FLIGHT (or, in
   `OnConflict: Wait` mode, the wait timed out). A 409 rate spike is a client retry storm:
   find the caller re-firing before its first attempt finishes.
2. `422 Unprocessable Entity` = the same key arrived with a DIFFERENT payload
   (`Fingerprint: Strict`, the default). A rising 422 rate is a client integration bug —
   keys being reused across distinct operations, not a server problem.
3. A replayed response carries the `Goldpath-Idempotent-Replay` header; the replay rate is
   readable from access logs by that header when you need to size a storm.
4. Inspect the store directly — the key shape is `http:{tenant}:{METHOD}:{path}:{key}`:
   - Redis: `KEYS *http:*` and `TTL <key>` (entries expire after `TtlHours`, default 24)
   - SQL distributed cache: `SELECT Id, ExpiresAtTime FROM <cache table> WHERE Id LIKE '%http:%';`

## Store growth / TTL tuning
The store is whatever `IDistributedCache` the host composes (Mediant's store rides it; the
module owns NO table). Growth = request rate × `TtlHours`; responses are stored only on 2xx,
byte-for-byte. Shorten `TtlHours` before shopping for a bigger store — a TTL longer than the
longest honest client retry window buys nothing but memory.

## Lock-ups in `OnConflict: Wait` mode
`Wait` holds the duplicate until the first attempt completes — a slow handler converts a
retry storm into a connection-pool drain, silently. If p99 latency climbs while 409s stay
flat, check for waiters queued behind one stuck request; prefer `Reject` (the default) for
anything long-running.

## Signals
No dedicated meter yet — watch `http.server.request.duration` sliced by status code (409/422
carry the story above), plus the store's own health (Redis `INFO memory`, cache-table row
count). The command path (`[Idempotent]`) shares the store and the semantics; its conflicts
surface as Mediant behavior results, not HTTP statuses.
