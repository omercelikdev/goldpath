using MassTransit;

namespace CorPay.Api.Orders.Features;

[HttpEndpoint("POST", "/api/v1/orders")]
public record CreateOrderCommand(string Reference, decimal Amount) : ICommand<Result<long>>;

public class CreateOrderHandler(OrdersDbContext db, IPublishEndpoint publisher)
    : ICommandHandler<CreateOrderCommand, Result<long>>
{
    public async ValueTask<Result<long>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new Order { Reference = request.Reference, Amount = request.Amount };
        db.Orders.Add(order);

        // ONE transaction around both saves: the identity id needs the first save, the
        // outbox row lands with the second — without the explicit transaction a crash in
        // between commits the order and LOSES the event (the exact failure the outbox
        // pattern exists to prevent).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);           // order row (id materializes)
        await publisher.Publish(new OrderPlaced(order.Id), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);           // outbox row — same transaction
        await transaction.CommitAsync(cancellationToken);       // order + event commit together

        return Result.Success(order.Id);
    }
}
