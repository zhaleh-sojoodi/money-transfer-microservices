using Contracts;
using MassTransit;
using RiskWorker.Services.IServices;

namespace RiskWorker.Consumers;

// MassTransit calls Consume once for every TransferRequested message in the "transfer-requested" queue.
// Like a controller, it stays thin: read the message, ask the evaluator, publish the result.
public class TransferRequestedConsumer : IConsumer<TransferRequested>
{
    private readonly IRiskEvaluator _riskEvaluator;
    private readonly ILogger<TransferRequestedConsumer> _logger;

    public TransferRequestedConsumer(IRiskEvaluator riskEvaluator, ILogger<TransferRequestedConsumer> logger)
    {
        _riskEvaluator = riskEvaluator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TransferRequested> context)
    {
        var transfer = context.Message;
        var decision = _riskEvaluator.Evaluate(transfer);

        // context.Publish (instead of injecting IPublishEndpoint) links the new message to the one being
        // consumed (same ConversationId), so the whole flow can be traced across services.
        if (decision.IsApproved)
        {
            await context.Publish(new TransferApproved(
                transfer.TransferId,
                transfer.FromAccount,
                transfer.ToAccount,
                transfer.Amount,
                transfer.Currency,
                DateTime.UtcNow));

            _logger.LogInformation("Transfer {TransferId} approved", transfer.TransferId);
        }
        else
        {
            await context.Publish(new TransferRejected(
                transfer.TransferId,
                transfer.FromAccount,
                transfer.ToAccount,
                transfer.Amount,
                transfer.Currency,
                decision.Reason!,
                DateTime.UtcNow));

            _logger.LogInformation("Transfer {TransferId} rejected: {Reason}", transfer.TransferId, decision.Reason);
        }
    }
}
