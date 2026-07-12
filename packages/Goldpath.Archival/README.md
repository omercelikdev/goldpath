# Goldpath.Archival

The data lifecycle after "hot": declarative retention per aggregate, archive runs that move
whole GRAPHS into a tamper-evident same-database store (SHA-256 hash chain), row-retention
purges for high-volume detail, legal hold, and erasure that composes with retention. Runs
execute on **Goldpath.Jobs** — chunked, checkpointed, resumable, visible in the same console.

```csharp
builder.AddGoldpathArchival<WebApplicationBuilder, OrdersDbContext>(archival =>
{
    archival.AddArchive<Claim>(a => a
        .Graph(c => c.Decisions, c => c.Documents)
        .Key(c => c.Id)
        .DueWhen(c => c.ClosedAt != null, c => c.ClosedAt!.Value)
        .ArchiveAfter(TimeSpan.FromDays(365))
        .RetainFor(years: 10)
        .DeleteHotRowsAfterArchive());

    archival.AddRowRetention<RatedUsageDetail>(r => r
        .After(TimeSpan.FromDays(90), u => u.RecordedAt)
        .Where(u => u.RolledUp));           // never purge unsummarized detail
});

builder.AddGoldpathJobs<WebApplicationBuilder, OrdersDbContext>(jobs =>
    jobs.AddGoldpathArchivalJobs<OrdersDbContext>());

// DbContext: modelBuilder.AddGoldpathArchiveModel();
```

Integrity model: every entry seals a **ChainHash** at append (never changes; the chain links
through it) and carries a **ContentHash** of the current document. The audited erasure path
(S2) redacts classified fields and re-stamps the content hash — divergence WITH an erasure
record is evidence, divergence WITHOUT one is tamper, and the verify job files both into
the repair queue. Retention purges remove only a contiguous chain PREFIX; an active legal
hold stops the purge at itself.

Admin API (retrieve / hold / erase / verify) arrives with S2; see docs/rfc/goldpath-archival.md.
