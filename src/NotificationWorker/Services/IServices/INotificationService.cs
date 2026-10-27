using NotificationWorker.Domain.Entities;
using NotificationWorker.Domain.Enums;

namespace NotificationWorker.Services.IServices;

public interface INotificationServices
{
    Task<Notification?> CreateAsync(Guid transferId, NotificationType Type, string recipient, 
        string message, CancellationToken cancellationToken = default);
}