namespace CorPay.Api.Orders;

public enum OrderStatus
{
    Pending,
    Confirmed,
}

public partial class Order : IAuditedEntity
{
    public long Id { get; set; }
    public string Reference { get; set; } = "";
    public decimal Amount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
