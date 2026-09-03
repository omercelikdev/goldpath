# Abstractions — Ops

N/A by construction (RFC §6): `Goldpath.Abstractions` is contracts only — headers, policies,
markers, the tenant and user contexts, `IIntegrationEvent`. It has no runtime behaviour,
emits no signal and owns no table. The packages that IMPLEMENT these contracts carry the
runbooks: tenancy (`Goldpath.MultiTenancy/ops`), auth policies (`Goldpath.Auth/ops`),
integration events (`Goldpath.Messaging/ops`). This file exists so an audit of `ops/`
directories does not read the absence as a gap.
