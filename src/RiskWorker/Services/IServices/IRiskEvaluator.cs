using Contracts;

namespace RiskWorker.Services.IServices;

public interface IRiskEvaluator
{
    RiskDecision Evaluate(TransferRequested transfer);
}

public record RiskDecision(bool IsApproved, string? Reason)
{
    public static RiskDecision Approve() => new(true, null);
    public static RiskDecision Reject(string reason) => new(false, reason);
}
