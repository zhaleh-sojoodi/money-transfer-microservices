namespace Contracts;

// Published by RiskWorker. Consumed by TransferApi (status update) 
public record TransferApproved(
    Guid TransferId,
    string FromAccount,
    string ToAccount,
    decimal Amount,
    string Currency,
    DateTime ApprovedAtUtc);
