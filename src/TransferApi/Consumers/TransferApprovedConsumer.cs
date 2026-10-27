using Contracts;
using MassTransit;
using TransferApi.Services.IServices;

namespace TransferApi.Consumers;

public class TransferApprovedConsumer : IConsumer<TransferApproved>
{
    private readonly ITransferService _transferService;

    public TransferApprovedConsumer(ITransferService transferService)
    {
        _transferService = transferService;
    }

    public async Task Consume(ConsumeContext<TransferApproved> context)
    {
        var transferApproved = context.Message;
        await _transferService.ApproveAsync(transferApproved.TransferId, context.CancellationToken);
    }
} 