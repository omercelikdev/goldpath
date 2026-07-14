namespace CorPay.Api.Payments;

public enum PaymentStatus
{
    Submitted,
    PendingApproval,
    Executed,
    Rejected,
    Failed,
}

/// <summary>
/// One corporate payment instruction — the finance card's atom. State changes are audited
/// (partial: AuditTrail), reads are tenant-fenced (partial: MultiTenancy), and the IBANs
/// are classified personal data (partial: DataProtection).
/// </summary>
public partial class PaymentInstruction : IAuditedEntity
{
    public long Id { get; set; }

    /// <summary>Treasurer-supplied end-to-end reference — unique per tenant (double-submit guard).</summary>
    public string Reference { get; set; } = "";

    public decimal Amount { get; set; }

    /// <summary>ISO 4217; the whitelist lives in the submit validator.</summary>
    public string Currency { get; set; } = "";

    public PaymentStatus Status { get; set; } = PaymentStatus.Submitted;

    /// <summary>Core-banking receipt for the executed payment (evidence, not a foreign key).</summary>
    public string? ExecutionReceipt { get; set; }

    /// <summary>The SECOND pair of eyes (four-eyes flow) — never the submitter.</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>Required when rejected — the auditor reads the why here and in the change rows.</summary>
    public string? RejectionReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
