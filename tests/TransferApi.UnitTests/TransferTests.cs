using TransferApi.Domain.Entities;
using TransferApi.Domain.Enums;

namespace TransferApi.UnitTests;

public class TransferTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesPendingTransfer()
    {
        var transfer = new Transfer("ACC123", "ACC456", 100.50m, "eur");

        Assert.NotEqual(Guid.Empty, transfer.Id);
        Assert.Equal(100.50m, transfer.Amount);
        Assert.Equal("EUR", transfer.Currency);
        Assert.Equal(TransferStatus.Pending, transfer.Status);
        Assert.Equal(DateTimeKind.Utc, transfer.CreatedAtUtc.Kind);
    }

    [Fact]
    public void Constructor_AmountWithThreeDecimals_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => new Transfer("ACC123", "ACC456", 100.555m, "EUR"));

        Assert.Equal("amount", ex.ParamName);
    }

    [Fact]
    public void Constructor_InvalidCurrency_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => new Transfer("ACC123", "ACC456", 100.00m, "INVALID"));

        Assert.Equal("currency", ex.ParamName);
    }
}
