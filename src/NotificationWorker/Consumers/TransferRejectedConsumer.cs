using Contracts;
using MassTransit;
using NotificationWorker.Domain.Enums;
using NotificationWorker.Services.IServices;

namespace NotificationWorker.Consumers;

public class TransferRejectedConsumer : IConsumer<TransferRejected>
{
    private readonly INotificationServices _notificationService;

    public TransferRejectedConsumer(INotificationServices notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task Consume(ConsumeContext<TransferRejected> context)
    {
        var transferRejected = context.Message;

        var recipient = transferRejected.FromAccount;

        var message = $"Your transfer of {transferRejected.Amount:0.00} {transferRejected.Currency} " +
                      $"to {transferRejected.ToAccount} was rejected: {transferRejected.Reason}";

        await _notificationService.CreateAsync(
            transferRejected.TransferId,
            NotificationType.TransferRejected,
            recipient,
            message,
            context.CancellationToken);
    }
}
