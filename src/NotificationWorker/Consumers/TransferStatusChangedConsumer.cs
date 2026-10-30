using Contracts;
using MassTransit;
using NotificationWorker.Domain.Enums;
using NotificationWorker.Services.IServices;

namespace NotificationWorker.Consumers;

public class TransferStatusChangedConsumer : IConsumer<TransferStatusChanged>
{
    private readonly INotificationServices _notificationService;

    public TransferStatusChangedConsumer(INotificationServices notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task Consume(ConsumeContext<TransferStatusChanged> context)
    {
        var transferStatusChanged = context.Message;

        var recipient = transferStatusChanged.FromAccount;


        var (type, ending) = transferStatusChanged.Status switch
        {
            "Approved" => (NotificationType.TransferApproved, "was approved."),
            "Rejected" => (NotificationType.TransferRejected, $"was rejected: {transferStatusChanged.Reason}"),
            _ => throw new ArgumentException($"Unknown status: {transferStatusChanged.Status}")
        };

        var message = $"Your transfer of {transferStatusChanged.Amount:0.00} {transferStatusChanged.Currency} to {transferStatusChanged.ToAccount} {ending}";


        await _notificationService.CreateAsync(
            transferStatusChanged.TransferId,
            type,
            recipient,
            message,
            context.CancellationToken);
    }
}
