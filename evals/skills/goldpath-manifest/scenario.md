# Eval: goldpath-manifest — toggle a capability truthfully

**Fixture:** a freshly generated default app.
**Input:** "Enable soft delete for this app."

**Acceptance (`accept.sh <APP_DIR>`):** manifest gains `softDelete: true`; the Api csproj
references `Goldpath.SoftDelete`; `AddGoldpathSoftDelete` is registered and `ApplyGoldpathSoftDelete` is
in the model; `specdrift validate` + `drift` clean; build + tests green.
