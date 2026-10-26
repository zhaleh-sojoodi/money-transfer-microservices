using Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using RiskWorker.Consumers;
using RiskWorker.Services;
using RiskWorker.Services.IServices;

namespace RiskWorker.UnitTests;

// Uses MassTransit's test harness: an in-memory bus instead of RabbitMQ.
// It records every message consumed and published, so the tests can ask "was X published?".
public class TransferRequestedConsumerTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddScoped<IRiskEvaluator, RiskEvaluator>()
            .AddMassTransitTestHarness(x => x.AddConsumer<TransferRequestedConsumer>())
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Consume_AmountWithinLimit_PublishesTransferApproved()
    {
        var message = CreateMessage(50.00m);

        await _harness.Bus.Publish(message);

        Assert.True(await _harness.GetConsumerHarness<TransferRequestedConsumer>().Consumed.Any<TransferRequested>());
        Assert.True(await _harness.Published.Any<TransferApproved>(x => x.Context.Message.TransferId == message.TransferId));
        Assert.False(await _harness.Published.Any<TransferRejected>());
    }

    [Fact]
    public async Task Consume_AmountAboveLimit_PublishesTransferRejectedWithReason()
    {
        var message = CreateMessage(20_000.00m);

        await _harness.Bus.Publish(message);

        Assert.True(await _harness.GetConsumerHarness<TransferRequestedConsumer>().Consumed.Any<TransferRequested>());
        // Any() waits until the message shows up (or times out); Select() only reads what is already recorded.
        Assert.True(await _harness.Published.Any<TransferRejected>());
        var rejected = _harness.Published.Select<TransferRejected>().Single().Context.Message;
        Assert.Equal(message.TransferId, rejected.TransferId);
        Assert.Equal("Amount exceeds the limit of 10,000.", rejected.Reason);
        Assert.False(await _harness.Published.Any<TransferApproved>());
    }

    private static TransferRequested CreateMessage(decimal amount) =>
        new(Guid.NewGuid(), "ACC123", "ACC456", amount, "EUR", DateTime.UtcNow);
}
