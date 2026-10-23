namespace Contracts;

// Published by RiskWorker. Consumed by TransferApi (status update) and NotificationWorker.
public record TransferRejected(
    Guid TransferId,
    string FromAccount,
    string ToAccount,
    decimal Amount,
    string Currency,
    string Reason,
    DateTime RejectedAtUtc);
