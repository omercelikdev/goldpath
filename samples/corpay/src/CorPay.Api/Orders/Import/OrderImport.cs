using Goldpath;

namespace CorPay.Api.Orders.Import;

/// <summary>
/// One row of the order-import file (features.bulk sample). Columns map by HEADER NAME —
/// a reordered export cannot shift values between fields.
/// </summary>
public sealed class OrderImportRow
{
    /// <summary>The business reference — also the in-file duplicate key.</summary>
    public string Reference { get; set; } = "";

    /// <summary>Order amount (validated positive).</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// Executes one APPROVED, VALID row. Throwing sends the row to the run's repair queue
/// (the jobs `replay-items` verb retries it); the chunk continues. Do NOT call
/// SaveChanges here — the engine writes row state batched per chunk (GP1502).
/// </summary>
public sealed class OrderImportHandler(OrdersDbContext db) : IGoldpathBulkRowHandler<OrderImportRow>
{
    /// <inheritdoc />
    public Task ExecuteAsync(OrderImportRow row, GoldpathBulkRowContext context, CancellationToken cancellationToken)
    {
        db.Orders.Add(new Order { Reference = row.Reference, Amount = row.Amount });
        return Task.CompletedTask;   // the chunk's batched SaveChanges persists it
    }
}
