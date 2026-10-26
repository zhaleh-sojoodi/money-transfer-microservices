using Contracts;
using RiskWorker.Services;

namespace RiskWorker.UnitTests;

public class RiskEvaluatorTests
{
    private readonly RiskEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_AmountAtLimit_Approves()
    {
        var decision = _evaluator.Evaluate(CreateMessage(10_000.00m));

        Assert.True(decision.IsApproved);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void Evaluate_AmountAboveLimit_RejectsWithReason()
    {
        var decision = _evaluator.Evaluate(CreateMessage(10_000.01m));

        Assert.False(decision.IsApproved);
        Assert.Equal("Amount exceeds the limit of 10,000.", decision.Reason);
    }

    private static TransferRequested CreateMessage(decimal amount) =>
        new(Guid.NewGuid(), "ACC123", "ACC456", amount, "EUR", DateTime.UtcNow);
}
