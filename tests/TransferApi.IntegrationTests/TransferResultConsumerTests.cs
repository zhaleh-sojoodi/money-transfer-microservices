using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Contracts;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransferApi.Data;
using TransferApi.Domain.Requests;

namespace TransferApi.IntegrationTests;

// Plays the role of RiskWorker: publishes TransferApproved / TransferRejected to the test RabbitMQ
// and checks that TransferApi's consumers update the transfer's status.
public class TransferResultConsumerTests : IClassFixture<TransferApiFactory>
{
    private readonly TransferApiFactory _factory;
    private readonly HttpClient _client;

    public TransferResultConsumerTests(TransferApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TransferApproved_PendingTransfer_StatusBecomesApproved()
    {
        var transfer = await CreateTransferAsync();

        await PublishAsync(new TransferApproved(
            transfer.Id, transfer.FromAccount, transfer.ToAccount, transfer.Amount, transfer.Currency, DateTime.UtcNow));

        var body = await WaitForStatusAsync(transfer.Id, "Approved");
        Assert.Equal(JsonValueKind.Null, body.GetProperty("rejectionReason").ValueKind);
    }

    [Fact]
    public async Task TransferRejected_PendingTransfer_StatusBecomesRejectedWithReason()
    {
        var transfer = await CreateTransferAsync();

        await PublishAsync(new TransferRejected(
            transfer.Id, transfer.FromAccount, transfer.ToAccount, transfer.Amount, transfer.Currency,
            "Amount exceeds the limit of 10,000.", DateTime.UtcNow));

        var body = await WaitForStatusAsync(transfer.Id, "Rejected");
        Assert.Equal("Amount exceeds the limit of 10,000.", body.GetProperty("rejectionReason").GetString());
    }

    [Fact]
    public async Task TransferApproved_PendingTransfer_PublishesTransferStatusChanged()
    {
        var changes = Channel.CreateUnbounded<TransferStatusChanged>();
        var subscription = await SubscribeAsync(changes);
        try
        {
            var transfer = await CreateTransferAsync();

            await PublishAsync(new TransferApproved(
                transfer.Id, transfer.FromAccount, transfer.ToAccount, transfer.Amount, transfer.Currency, DateTime.UtcNow));

            var message = await WaitForMessageAsync(changes, m => m.TransferId == transfer.Id, seconds: 10);
            Assert.Equal("Approved", message.Status);
            Assert.Null(message.Reason);
            Assert.Equal(transfer.FromAccount, message.FromAccount);
            Assert.Equal(transfer.Amount, message.Amount);
        }
        finally
        {
            await subscription.StopAsync();
        }
    }

    [Fact]
    public async Task TransferRejected_PendingTransfer_PublishesTransferStatusChangedWithReason()
    {
        var changes = Channel.CreateUnbounded<TransferStatusChanged>();
        var subscription = await SubscribeAsync(changes);
        try
        {
            var transfer = await CreateTransferAsync();

            await PublishAsync(new TransferRejected(
                transfer.Id, transfer.FromAccount, transfer.ToAccount, transfer.Amount, transfer.Currency,
                "Amount exceeds the limit of 10,000.", DateTime.UtcNow));

            var message = await WaitForMessageAsync(changes, m => m.TransferId == transfer.Id, seconds: 10);
            Assert.Equal("Rejected", message.Status);
            Assert.Equal("Amount exceeds the limit of 10,000.", message.Reason);
        }
        finally
        {
            await subscription.StopAsync();
        }
    }

    // inbox_state is only written by UseEntityFrameworkOutbox, so a row here proves the consumer
    // ran inside the outbox transaction. Without that setting this code still compiles and appears to work.
    [Fact]
    public async Task TransferApproved_PendingTransfer_RunsThroughConsumerOutbox()
    {
        var transfer = await CreateTransferAsync();
        var messageId = NewId.NextGuid();

        await _factory.Services.GetRequiredService<IBus>().Publish(
            new TransferApproved(transfer.Id, transfer.FromAccount, transfer.ToAccount, transfer.Amount, transfer.Currency, DateTime.UtcNow),
            context => context.MessageId = messageId);

        await WaitForStatusAsync(transfer.Id, "Approved");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransferDbContext>();
        Assert.True(await db.Set<InboxState>().AnyAsync(s => s.MessageId == messageId));
    }

    // The original design bug: a failed status update must not lead to a notification.
    // Approving a Rejected transfer throws, so after all retries MassTransit publishes a Fault.
    [Fact]
    public async Task TransferApproved_RejectedTransfer_FailsAndPublishesNoStatusChange()
    {
        var changes = Channel.CreateUnbounded<TransferStatusChanged>();
        var faults = Channel.CreateUnbounded<Fault<TransferApproved>>();
        var changeSubscription = await SubscribeAsync(changes);
        var faultSubscription = await SubscribeAsync(faults);
        try
        {
            var transfer = await CreateTransferAsync();
            await PublishAsync(new TransferRejected(
                transfer.Id, transfer.FromAccount, transfer.ToAccount, transfer.Amount, transfer.Currency,
                "Amount exceeds the limit of 10,000.", DateTime.UtcNow));
            await WaitForMessageAsync(changes, m => m.TransferId == transfer.Id, seconds: 10);

            await PublishAsync(new TransferApproved(
                transfer.Id, transfer.FromAccount, transfer.ToAccount, transfer.Amount, transfer.Currency, DateTime.UtcNow));

            // 3 retries, 5 seconds apart, happen first.
            await WaitForMessageAsync(faults, f => f.Message.TransferId == transfer.Id, seconds: 40);

            while (changes.Reader.TryRead(out var extra))
                Assert.NotEqual(transfer.Id, extra.TransferId);

            await WaitForStatusAsync(transfer.Id, "Rejected");
        }
        finally
        {
            await faultSubscription.StopAsync();
            await changeSubscription.StopAsync();
        }
    }

    // A temporary queue bound to T's exchange. Must be connected BEFORE the message is published:
    // without a bound queue, RabbitMQ drops it.
    private async Task<HostReceiveEndpointHandle> SubscribeAsync<T>(Channel<T> received) where T : class
    {
        var handle = _factory.Services.GetRequiredService<IBus>().ConnectReceiveEndpoint(e =>
            e.Handler<T>(context => received.Writer.WriteAsync(context.Message).AsTask()));
        await handle.Ready;
        return handle;
    }

    // Other tests in this class also publish, so skip messages about other transfers.
    private static async Task<T> WaitForMessageAsync<T>(Channel<T> received, Func<T, bool> matches, int seconds)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        T message;
        do
        {
            message = await received.Reader.ReadAsync(timeout.Token);
        } while (!matches(message));

        return message;
    }

    private async Task<CreatedTransfer> CreateTransferAsync()
    {
        var request = new CreateTransferRequest
        {
            FromAccount = "ACC123",
            ToAccount = "ACC456",
            Amount = 50.00m,
            Currency = "EUR"
        };
        var response = await _client.PostAsJsonAsync("/transfers", request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return new CreatedTransfer(body.GetProperty("id").GetGuid(), request.FromAccount, request.ToAccount, request.Amount, "EUR");
    }

    private Task PublishAsync<T>(T message) where T : class =>
        _factory.Services.GetRequiredService<IBus>().Publish(message);

    // The status changes asynchronously (RabbitMQ -> consumer -> database), so poll GET until it matches.
    private async Task<JsonElement> WaitForStatusAsync(Guid id, string expectedStatus)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        JsonElement body;
        do
        {
            body = await _client.GetFromJsonAsync<JsonElement>($"/transfers/{id}");
            if (body.GetProperty("status").GetString() == expectedStatus)
                return body;

            await Task.Delay(200);
        } while (DateTime.UtcNow < deadline);

        Assert.Fail($"Transfer {id} did not reach status {expectedStatus} within 10 seconds (last status: {body.GetProperty("status").GetString()}).");
        return body;
    }

    private record CreatedTransfer(Guid Id, string FromAccount, string ToAccount, decimal Amount, string Currency);
}
