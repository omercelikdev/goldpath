# Console — Ops Runbook

`MapGoldpathConsole()` serves the operations console from the package's embedded assets on
the app's own management head (`/goldpath/console/`), behind the ops floor
(`GoldpathPolicies.Ops`) or with the VISIBLE `exposeUnsecured: true` opt-out. The console is
a client of the frozen admin contract (`/goldpath/admin/*`) — it has no data of its own.

## "The page is blank / 500"
The page references its bundle relatively (`./assets/…`). A blank page with a green status
means the assets did not ship — a `Goldpath.Console` package built without its `dist`
(the GM lane fails on exactly this). Verify: `GET /goldpath/console/assets/<bundle>.js`
must answer 200; the served-console smoke asserts it on every train.

## "Every panel says refused / forbidden"
- `401` on the page itself: the ops floor is up and the operator has no principal. Sign in
  against the app's IdP; the console carries the browser's credentials (`credentials:
  include`), it holds no token of its own (open-threads T10).
- `403` on the calls: a principal without the `goldpath-ops` role. Grant the role, not an
  exemption.
- `400` "cannot be scoped": a multi-tenant app resolving tenants by HEADER; the console's
  calls carry none (T11). Subdomain resolution works today; `?tenant=` for holders of the
  all-tenants role.
- `absent` everywhere on a service that has modules: CORS. A cross-origin service must
  allow the console's ORIGIN (named, never reflected — CWE-942) with credentials.

## The registry (`console.config.json`)
Served next to the console; adding a service is a config change. A registry that cannot be
read falls back to same-origin AND says so in the shell — an operator who configured four
services and sees one has found the outage, not a bug in the console.

## Signals
- `LogWarning "{Prefix} is mapped WITHOUT the ops policy"` at startup: the opt-out is in
  effect on that prefix (`Goldpath.Sdk` guard). Acceptable only behind an authenticating
  boundary; in a log search this line is the inventory of unguarded heads.
- Request rate and 401/403 on `/goldpath/console/*` and `/goldpath/admin/*`
  (`http.server.request.duration` by `http_route`) — the console's traffic IS the operators'
  activity; a 401 spike is a token expiry wave or an attack.

## Dashboard
`grafana-console-dashboard.json` — admin-surface traffic and refusals by route, page serves,
latency of the admin reads (the console's own responsiveness ceiling).
