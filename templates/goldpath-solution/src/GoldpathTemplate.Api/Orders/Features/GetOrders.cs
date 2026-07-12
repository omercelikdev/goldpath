
namespace GoldpathTemplate.Api.Orders.Features;

// Init-property record: EF can trace projected members back to columns for the keyset
// ORDER BY. Positional (constructor) records cannot be translated — see Goldpath.Data README.
public record OrderDto
{
    public long Id { get; init; }
    public string Reference { get; init; } = "";
    public decimal Amount { get; init; }
    public OrderStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

[HttpEndpoint("GET", "/api/v1/orders")]
public record GetOrdersQuery : IQuery<Result<Page<OrderDto>>>
{
    // Parameterless + init-only: Mediant binds GET queries from the query string.
    public string? Cursor { get; init; }
    public int Size { get; init; } = PageRequest.DefaultSize;
}

public class GetOrdersHandler(OrdersDbContext db) : IQueryHandler<GetOrdersQuery, Result<Page<OrderDto>>>
{
    public async ValueTask<Result<Page<OrderDto>>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        // Keyset pagination on the canonical (CreatedAt, Id) pair — never OFFSET.
        var page = await db.Orders
            .Select(o => new OrderDto
            {
                Id = o.Id,
                Reference = o.Reference,
                Amount = o.Amount,
                Status = o.Status,
                CreatedAt = o.CreatedAt,
            })
            .ToPageAsync(new PageRequest(request.Cursor, request.Size),
                o => o.CreatedAt, o => o.Id, cancellationToken: cancellationToken);

        return Result.Success(page);
    }
}
