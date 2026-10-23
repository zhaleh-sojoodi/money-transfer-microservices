namespace Contracts;

// Published by TransferApi after a transfer is saved as Pending. Consumed by RiskWorker.
public record TransferRequested(
    Guid TransferId,
    string FromAccount,
    string ToAccount,
    decimal Amount,
    string Currency,
    DateTime RequestedAtUtc);
