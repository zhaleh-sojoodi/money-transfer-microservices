using NotificationWorker.Domain.Enums;

namespace NotificationWorker.Domain.Entities;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid TransferId { get; private set; }
    public NotificationType Type { get; private set; } 
    public string Recipient { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }

    private Notification() {}

    public Notification(Guid transferId, NotificationType type, string recipient, string message)
    {
        if (transferId == Guid.Empty)
            throw new ArgumentException("TransferId is required", nameof(transferId));
        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException("Recipient account is required.", nameof(recipient));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message account is required.", nameof(message));
        
        Id = Guid.NewGuid();
        TransferId = transferId;
        Type = type;
        Recipient = recipient;
        Message = message;
        CreatedAtUtc = DateTime.UtcNow;
    }
}