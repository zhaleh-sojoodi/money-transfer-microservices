using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TransferApi.Domain.Requests;

namespace TransferApi.IntegrationTests;

public class TransferControllerTests : IClassFixture<TransferApiFactory>
{
    private readonly HttpClient _client;

    public TransferControllerTests(TransferApiFactory factory)
    {
        _client = factory.CreateClient();
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
