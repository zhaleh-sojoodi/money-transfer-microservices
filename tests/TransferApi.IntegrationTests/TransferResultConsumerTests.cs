using System.Net.Http.Json;
using System.Text.Json;
using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
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
