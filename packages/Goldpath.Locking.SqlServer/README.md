# Goldpath.Locking.SqlServer

The SQL Server (`sp_getapplock`) provider for `Goldpath.Locking` — shipped as a separate package
ON PURPOSE.

## Why separate

The dependency chain (`DistributedLock.SqlServer` → `Microsoft.Data.SqlClient` →
`Microsoft.Data.SqlClient.SNI.runtime`) carries Microsoft's **proprietary-but-free** native
SNI bits (Microsoft Software License Terms: closed source, free of charge, redistributable).
The Goldpath license gate allows it as a REVIEWED exception scoped to this package only — teams
that choose SQL Server already run commercially-licensed Microsoft data infrastructure, and
teams that don't must not carry these bits in their graph. Core `Goldpath.Locking` stays fully
open source.

## Getting started

```csharp
builder.AddGoldpathSqlServerLocking();    // instead of AddGoldpathLocking()
```

Configuration binds from the same `Goldpath:DistributedLocking` section; `ConnectionName`
defaults to `database` (the lock lives in the app database — zero new infrastructure).
Everything else — `GoldpathLockNames`, metrics, usage shape — is identical to the core package.

## Configuration

See `Goldpath.Locking`.

## Providers

This package IS the provider. Redis/Postgres live in `Goldpath.Locking`.
