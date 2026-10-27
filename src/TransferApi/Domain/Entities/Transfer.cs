using TransferApi.Domain.Enums;

namespace TransferApi.Domain.Entities;

public class Transfer
{
    public Guid Id { get; private set; }
    public string FromAccount { get; private set; } = null!;
    public string ToAccount { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public TransferStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public string? RejectionReason {get; private set; } 

    private Transfer() { }

    public Transfer(string fromAccount, string toAccount, decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(fromAccount))
            throw new ArgumentException("From account is required.", nameof(fromAccount));
        if (string.IsNullOrWhiteSpace(toAccount))
            throw new ArgumentException("To account is required.", nameof(toAccount));
        if (fromAccount == toAccount)
            throw new ArgumentException("From and to accounts must differ.", nameof(toAccount));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        if (decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Amount cannot have more than 2 decimal places.", nameof(amount));
        if (currency is null || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));

        Id = Guid.NewGuid();
        FromAccount = fromAccount;
        ToAccount = toAccount;
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        Status = TransferStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public void Approve()
    {
        if (Status == TransferStatus.Approved) return;
        if (Status != TransferStatus.Pending)
            throw new InvalidOperationException($"Cannot approve a transfer that is {Status}.");

        Status = TransferStatus.Approved;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string reason) 
    {
        if (Status == TransferStatus.Rejected) return;
        if (Status != TransferStatus.Pending)
            throw new InvalidOperationException($"Cannot reject a transfer that is {Status}.");
        if (string.IsNullOrEmpty(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));

        Status = TransferStatus.Rejected;
        UpdatedAtUtc = DateTime.UtcNow;
        RejectionReason  = reason;
    }
}
