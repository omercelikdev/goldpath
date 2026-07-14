using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CorPay.EodWorker.Reports;

/// <summary>READ-ONLY map of the Api-owned instructions table (D3: map, never own).</summary>
public class PaymentInstructionRead
{
    public long Id { get; set; }
    public string Reference { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public string TenantId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>READ-ONLY map of the Api-owned ledger-feed table (D3: map, never own).</summary>
public class LedgerFeedRead
{
    public long Id { get; set; }
    public string Reference { get; set; } = "";
    public DateTimeOffset FedAt { get; set; }
}

/// <summary>One reconciled business day per tenant — the regulator-ready day evidence.</summary>
public class EodReconciliationRow
{
    public long Id { get; set; }
    public DateOnly Day { get; set; }
    public string TenantId { get; set; } = "";
    public int Executed { get; set; }
    public int Matched { get; set; }
    public int Mismatched { get; set; }
    public DateTimeOffset ReconciledAt { get; set; }
}

/// <summary>A single instruction whose books do not balance — the repair queue's payload.</summary>
public sealed record EodMismatch(string Reference, string Reason);

/// <summary>
/// The finance card's hardest customer: nightly (banking days ONLY — the calendar skips
/// weekends/holidays), one chunk per tenant so a poisoned tenant never blocks the rest,
/// checkpointed so a killed run RESUMES, deadline-tracked so the 07:00 breach is
/// predicted before it happens. Mismatches land in the jobs repair queue; replay
/// re-checks a single reference (late feed rows heal, true losses keep paging).
/// </summary>
public sealed class EodReconciliationJob : IGoldpathJob, IGoldpathItemReplay
{
    /// <inheritdoc />
    public async Task<GoldpathJobPlan> PlanAsync(GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var db = context.Services.GetRequiredService<ReportsDbContext>();
        var (from, to) = Window(DateTimeOffset.UtcNow);
        var tenants = await db.Set<PaymentInstructionRead>().AsNoTracking()
            .Where(i => i.Status == "Executed" && i.CreatedAt >= from && i.CreatedAt < to)   // enum-as-string convention: the STORED value
            .Select(i => i.TenantId)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(cancellationToken);

        return new GoldpathJobPlan([.. tenants.Select(t => $"{DateOnly.FromDateTime(from.UtcDateTime):O}|{t}")], tenants.Count);
    }

    /// <inheritdoc />
    public async Task ExecuteChunkAsync(GoldpathJobChunk chunk, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var separator = chunk.Payload.IndexOf('|');
        var day = DateOnly.Parse(chunk.Payload[..separator]);
        var tenant = chunk.Payload[(separator + 1)..];
        var db = context.Services.GetRequiredService<ReportsDbContext>();

        var from = new DateTimeOffset(day, TimeOnly.MinValue, TimeSpan.Zero);
        var to = from.AddDays(1);
        var executed = await db.Set<PaymentInstructionRead>().AsNoTracking()
            .Where(i => i.TenantId == tenant && i.Status == "Executed"
                && i.CreatedAt >= from && i.CreatedAt < to)
            .Select(i => i.Reference)
            .ToListAsync(cancellationToken);
        var fed = await db.Set<LedgerFeedRead>().AsNoTracking()
            .Where(f => f.FedAt >= from && f.FedAt < to.AddHours(1))   // the feed may lag the day boundary
            .Select(f => f.Reference)
            .ToListAsync(cancellationToken);

        var mismatches = Reconcile(executed, fed);
        foreach (var mismatch in mismatches)
        {
            chunk.ReportItemFailure($"{tenant}|{mismatch.Reference}", mismatch.Reason);
        }

        db.Set<EodReconciliationRow>().Add(new EodReconciliationRow
        {
            Day = day,
            TenantId = tenant,
            Executed = executed.Count,
            Matched = executed.Count - mismatches.Count,
            Mismatched = mismatches.Count,
            ReconciledAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);   // one batched write per chunk (house rule)
    }

    /// <inheritdoc />
    public async Task ReplayItemAsync(string itemKey, GoldpathJobContext context, CancellationToken cancellationToken)
    {
        var separator = itemKey.IndexOf('|');
        var reference = itemKey[(separator + 1)..];
        var db = context.Services.GetRequiredService<ReportsDbContext>();
        var healed = await db.Set<LedgerFeedRead>().AsNoTracking()
            .AnyAsync(f => f.Reference == reference, cancellationToken);
        if (!healed)
        {
            throw new InvalidOperationException(
                $"'{reference}' is still missing from the ledger feed — confirm with core banking before replaying again");
        }
    }

    /// <summary>Pure reconciliation: executed-but-never-fed is money the ledger never heard of.</summary>
    internal static List<EodMismatch> Reconcile(IReadOnlyList<string> executedReferences, IReadOnlyList<string> fedReferences)
    {
        var fed = new HashSet<string>(fedReferences, StringComparer.Ordinal);
        return [.. executedReferences
            .Where(reference => !fed.Contains(reference))
            .Select(reference => new EodMismatch(reference, "executed but never reached the ledger feed"))];
    }

    /// <summary>Yesterday's business day in UTC — the window EOD closes.</summary>
    internal static (DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        var from = new DateTimeOffset(day, TimeOnly.MinValue, TimeSpan.Zero);
        return (from, from.AddDays(1));
    }
}
