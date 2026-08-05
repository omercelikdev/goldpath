#if (UseBroker)
using MassTransit;
#endif

namespace GoldpathTemplate.Application.Orders.Features;

[HttpEndpoint("POST", "/api/v1/orders")]
#if (UseIdempotency)
// The golden path marks every write-performing command (GP1001 holds the composition to
// it): a client retry replays the stored answer instead of creating a second order, and
// the business reference — not the whole payload — is the key.
[Mediant.Behaviors.Attributes.Idempotent(KeyProperty = nameof(CreateOrderCommand.Reference))]
#endif
public record CreateOrderCommand(string Reference, decimal Amount) : ICommand<Result<long>>;

#if (UseBroker)
public class CreateOrderHandler(IOrdersDbContext db, IPublishEndpoint publisher)
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
#else
public class CreateOrderHandler(IOrdersDbContext db)
    : ICommandHandler<CreateOrderCommand, Result<long>>
{
    public async ValueTask<Result<long>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // No broker in this shape: the order is confirmed synchronously.
        var order = new Order { Reference = request.Reference, Amount = request.Amount, Status = OrderStatus.Confirmed };
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(order.Id);
    }
}
#endif
