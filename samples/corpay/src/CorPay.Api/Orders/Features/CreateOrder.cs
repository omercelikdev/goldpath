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

        await db.SaveChangesAsync(cancellationToken);           // order row + outbox row, atomically
        await publisher.Publish(new OrderPlaced(order.Id), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);           // flush the bus outbox

        return Result.Success(order.Id);
    }
}
