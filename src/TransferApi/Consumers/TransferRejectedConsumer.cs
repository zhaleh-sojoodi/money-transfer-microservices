using Contracts;
using MassTransit;
using TransferApi.Services.IServices;

namespace TransferApi.Consumers;

public class TransferRejectedConsumer : IConsumer<TransferRejected>
{
    private readonly ITransferService _transferService;

    public TransferRejectedConsumer(ITransferService transferService)
    {
        _transferService = transferService;
    }

    public async Task Consume(ConsumeContext<TransferRejected> context)
    {
        var transferRejected = context.Message;
        await _transferService.RejectAsync(transferRejected.TransferId, transferRejected.Reason, context.CancellationToken);
    }
} 