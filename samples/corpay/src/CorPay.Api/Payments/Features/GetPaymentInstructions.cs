namespace CorPay.Api.Payments.Features;

// Init-property record: EF traces projected members back to columns for the keyset
// ORDER BY (positional records cannot be translated — Goldpath.Data README).
public record PaymentInstructionDto
{
    public long Id { get; init; }
    public string Reference { get; init; } = "";
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "";
    public PaymentStatus Status { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

[HttpEndpoint("GET", "/api/v1/payment-instructions")]
public record GetPaymentInstructionsQuery : IQuery<Result<Page<PaymentInstructionDto>>>
{
    public string? Cursor { get; init; }
    public int Size { get; init; } = PageRequest.DefaultSize;

    /// <summary>Optional state filter (e.g. PendingApproval for the approver's worklist).</summary>
    public PaymentStatus? Status { get; init; }
}

/// <summary>Tenant-fenced by the live query filter; keyset on (CreatedAt, Id) — never OFFSET.</summary>
public class GetPaymentInstructionsHandler(Orders.OrdersDbContext db)
    : IQueryHandler<GetPaymentInstructionsQuery, Result<Page<PaymentInstructionDto>>>
{
    public async ValueTask<Result<Page<PaymentInstructionDto>>> Handle(GetPaymentInstructionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Set<PaymentInstruction>().AsQueryable();
        if (request.Status is { } status)
        {
            query = query.Where(i => i.Status == status);
        }

        var page = await query
            .Select(i => new PaymentInstructionDto
            {
                Id = i.Id,
                Reference = i.Reference,
                Amount = i.Amount,
                Currency = i.Currency,
                Status = i.Status,
                ApprovedBy = i.ApprovedBy,
                CreatedAt = i.CreatedAt,
            })
            .ToPageAsync(new PageRequest(request.Cursor, request.Size),
                i => i.CreatedAt, i => i.Id, cancellationToken: cancellationToken);

        return Result.Success(page);
    }
}
