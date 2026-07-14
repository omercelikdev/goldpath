namespace CorPay.Api.Payments;

// Every state change writes old → new change rows in the SAME transaction — the
// auditor's story of an instruction is these rows plus the approver stamps.
public partial class PaymentInstruction : IAuditLogged;
