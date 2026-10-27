using Contracts;
using MassTransit;
using NotificationWorker.Domain.Enums;
using NotificationWorker.Services.IServices;

namespace NotificationWorker.Consumers;

public class TransferApprovedConsumer : IConsumer<TransferApproved>
{
    private readonly INotificationServices _notificationService;

    public TransferApprovedConsumer(INotificationServices notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task Consume(ConsumeContext<TransferApproved> context)
    {
        var transferApproved = context.Message;

        var recipient = transferApproved.FromAccount;

        var message = $"Your transfer of {transferApproved.Amount:0.00} {transferApproved.Currency} " +
                      $"to {transferApproved.ToAccount} was approved.";

        await _notificationService.CreateAsync(
            transferApproved.TransferId,
            NotificationType.TransferApproved,
            recipient,
            message,
            context.CancellationToken);
    }
}
