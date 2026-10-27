using NotificationWorker.Domain.Entities;

namespace NotificationWorker.Repositories.IRepositories;

public interface INotificationRepository
{
    Task<Notification?> GetByTransferIdAsync(Guid transferId, CancellationToken cancellationToken = default);

    // Inserts and saves the notification. Returns false if a notification for the same transfer
    // already exists (unique index on transfer_id), e.g. when two copies of a message arrive at once.
    Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken = default);
}
