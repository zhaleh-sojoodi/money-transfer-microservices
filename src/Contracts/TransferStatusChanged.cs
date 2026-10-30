namespace Contracts;

// Published by TransferApi. Consumed by NotificationWorker 
public record TransferStatusChanged(
    Guid TransferId,
    string FromAccount,
    string ToAccount,
    decimal Amount,
    string Currency,
    string Status,
    string? Reason,
    DateTime ChangedAtUtc);
