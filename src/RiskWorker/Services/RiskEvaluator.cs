using Contracts;
using RiskWorker.Services.IServices;

namespace RiskWorker.Services;

// The risk rules. Plain logic with no MassTransit or RabbitMQ code, so it is easy to unit test.
public class RiskEvaluator : IRiskEvaluator
{
    public const decimal MaxAmount = 10_000m;

    public RiskDecision Evaluate(TransferRequested transfer)
    {
        if (transfer.Amount > MaxAmount)
            return RiskDecision.Reject($"Amount exceeds the limit of {MaxAmount:N0}.");

        return RiskDecision.Approve();
    }
}
