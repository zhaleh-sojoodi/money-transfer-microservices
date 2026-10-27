using NotificationWorker.Domain.Entities;
using NotificationWorker.Domain.Enums;
using NotificationWorker.Repositories.IRepositories;
using NotificationWorker.Services.IServices;

namespace NotificationWorker.Services;

public class NotificationService : INotificationServices
{
    private readonly INotificationRepository _notificationRepository;

    public NotificationService(INotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public async Task<Notification?> CreateAsync(Guid transferId, NotificationType type, string recipient, 
        string message, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.GetByTransferIdAsync(transferId, cancellationToken);

        if (existing is not null)
            return existing;          

        var notification = new Notification(transferId, type, recipient, message);

        if (await _notificationRepository.TryAddAsync(notification, cancellationToken))
            return notification;

        return await _notificationRepository.GetByTransferIdAsync(transferId, cancellationToken);
    }
}