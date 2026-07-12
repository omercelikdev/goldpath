# Goldpath.Templates

Golden-path templates. Current: `goldpath-solution` (GM-1 shape: modular-monolith, vertical-slice,
PostgreSQL, RabbitMQ — the manifest defaults). Further shapes arrive per the template RFC phasing
(`docs/rfc/goldpath-template.md`, decision D1).

```
dotnet new install Goldpath.Templates
dotnet new goldpath-solution -n MyPlatform
cd MyPlatform && dotnet run --project src/MyPlatform.AppHost   # F5 experience: containers up, dashboard on
dotnet test                                                     # smoke: POST → event consumed → paginated GET
```

Local validation without any feed: `scripts/validate-gm.sh <Name> [--db sqlserver --broker none]` packs the repo, installs
the template from source, generates the requested shape, builds it against the local feed,
and runs the smoke suite. Current proven shapes: GM-1 (defaults) and GM-4 (sqlserver+none).
