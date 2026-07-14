using Microsoft.EntityFrameworkCore;

namespace CorPay.Api.Payments.Features;

/// <summary>The treasurer's day at a glance — counts and sums per state, one query.</summary>
public record DayReportDto
{
    public DateOnly Day { get; init; }
    public int Executed { get; init; }
    public decimal ExecutedTotal { get; init; }
    public int PendingApproval { get; init; }
    public int Rejected { get; init; }
    public int LedgerFeedRows { get; init; }
}

[HttpEndpoint("GET", "/api/v1/payment-instructions/day-report")]
public record GetDayReportQuery : IQuery<Result<DayReportDto>>
{
    /// <summary>ISO date; defaults to today (UTC).</summary>
    public DateOnly? Day { get; init; }
}

/// <summary>
/// Tenant-fenced aggregate over the day's instructions PLUS the ledger-feed row count —
/// the same two numbers EOD reconciles; the human sees them any time of day.
/// </summary>
public class GetDayReportHandler(Orders.OrdersDbContext db, TimeProvider time)
    : IQueryHandler<GetDayReportQuery, Result<DayReportDto>>
{
    public async ValueTask<Result<DayReportDto>> Handle(GetDayReportQuery request, CancellationToken cancellationToken)
    {
        var day = request.Day ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var from = new DateTimeOffset(day, TimeOnly.MinValue, TimeSpan.Zero);
        var to = from.AddDays(1);

        var states = await db.Set<PaymentInstruction>()
            .Where(i => i.CreatedAt >= from && i.CreatedAt < to)
            .GroupBy(i => i.Status)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(i => i.Amount) })
            .ToListAsync(cancellationToken);
        var fed = await db.Set<LedgerFeedEntry>()
            .CountAsync(e => e.FedAt >= from && e.FedAt < to, cancellationToken);

        return Result.Success(new DayReportDto
        {
            Day = day,
            Executed = states.FirstOrDefault(s => s.Key == PaymentStatus.Executed)?.Count ?? 0,
            ExecutedTotal = states.FirstOrDefault(s => s.Key == PaymentStatus.Executed)?.Total ?? 0m,
            PendingApproval = states.FirstOrDefault(s => s.Key == PaymentStatus.PendingApproval)?.Count ?? 0,
            Rejected = states.FirstOrDefault(s => s.Key == PaymentStatus.Rejected)?.Count ?? 0,
            LedgerFeedRows = fed,
        });
    }
}
