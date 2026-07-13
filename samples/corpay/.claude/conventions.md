# Conventions — CorPay

- Language: code, identifiers, commits in English.
- Features: one FILE per feature under `<Area>/Features/` (`CancelOrder.cs` = command/query + handler + (validator) together). Split into a folder only when a feature genuinely outgrows one file.
- Commands/queries: Mediant records with `[HttpEndpoint]`; responses are `Result<T>`.
- Wire: camelCase JSON, enums as strings, cursor pagination (`items`/`nextCursor`/`size`).
- Headers: use `GoldpathHeaders` constants; never string literals.
- Tests: xunit; smoke tests drive the real AppHost (containers), no mocks for the happy path.
