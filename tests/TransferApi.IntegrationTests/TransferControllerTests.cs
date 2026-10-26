using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Contracts;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using TransferApi.Domain.Requests;

namespace TransferApi.IntegrationTests;

public class TransferControllerTests : IClassFixture<TransferApiFactory>
{
    private readonly TransferApiFactory _factory;
    private readonly HttpClient _client;

    public TransferControllerTests(TransferApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateTransfer_ValidRequest_PublishesTransferRequested()
    {
        // Subscribe a temporary queue to TransferRequested BEFORE posting:
        // without a bound queue, RabbitMQ drops the message.
        var received = Channel.CreateUnbounded<TransferRequested>();
        var bus = _factory.Services.GetRequiredService<IBus>();
        var handle = bus.ConnectReceiveEndpoint(e =>
            e.Handler<TransferRequested>(context => received.Writer.WriteAsync(context.Message).AsTask()));
        await handle.Ready;

        try
        {
            var request = new CreateTransferRequest
            {
                FromAccount = "ACC123",
                ToAccount = "ACC456",
                Amount = 75.25m,
                Currency = "EUR"
            };
            var response = await _client.PostAsJsonAsync("/transfers", request);
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            var transferId = created.GetProperty("id").GetGuid();

            // Messages arrive asynchronously (outbox -> RabbitMQ -> our queue). Other tests also publish,
            // so skip messages for other transfers. Fail after 10 seconds instead of hanging.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            TransferRequested message;
            do
            {
                message = await received.Reader.ReadAsync(timeout.Token);
            } while (message.TransferId != transferId);

            Assert.Equal("ACC123", message.FromAccount);
            Assert.Equal("ACC456", message.ToAccount);
            Assert.Equal(75.25m, message.Amount);
            Assert.Equal("EUR", message.Currency);
        }
        finally
        {
            await handle.StopAsync();
        }
    }

    [Fact]
    public async Task GetTransfer_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/transfers/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTransfer_AfterCreate_Returns200WithSameTransfer()
    {
        var request = new CreateTransferRequest
        {
            FromAccount = "ACC123",
            ToAccount = "ACC456",
            Amount = 250.50m,
            Currency = "eur"
        };
        var createResponse = await _client.PostAsJsonAsync("/transfers", request);

        var getResponse = await _client.GetAsync(createResponse.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ACC123", body.GetProperty("fromAccount").GetString());
        Assert.Equal("ACC456", body.GetProperty("toAccount").GetString());
        Assert.Equal(250.50m, body.GetProperty("amount").GetDecimal());
        Assert.Equal("EUR", body.GetProperty("currency").GetString());
        Assert.Equal("Pending", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreateTransfer_ValidRequest_Returns201WithLocation()
    {
        var request = new CreateTransferRequest
        {
            FromAccount = "ACC123",
            ToAccount = "ACC456",
            Amount = 100.00m,
            Currency = "USD"
        };

        var response = await _client.PostAsJsonAsync("/transfers", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task CreateTransfer_AmountWithThreeDecimals_Returns400()
    {
        var request = new CreateTransferRequest
        {
            FromAccount = "ACC123",
            ToAccount = "ACC456",
            Amount = 100.555m,
            Currency = "USD"
        };

        var response = await _client.PostAsJsonAsync("/transfers", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTransfer_SameAccounts_Returns400()
    {
        var request = new CreateTransferRequest
        {
            FromAccount = "ACC123",
            ToAccount = "ACC123",
            Amount = 100.00m,
            Currency = "USD"
        };

        var response = await _client.PostAsJsonAsync("/transfers", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTransfer_InvalidCurrency_Returns400()
    {
        var request = new CreateTransferRequest
        {
            FromAccount = "ACC123",
            ToAccount = "ACC456",
            Amount = 100.00m,
            Currency = "INVALID"
        };

        var response = await _client.PostAsJsonAsync("/transfers", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
