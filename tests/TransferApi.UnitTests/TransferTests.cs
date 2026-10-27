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

    [Fact]
    public void Approve_PendingTransfer_SetsApproved()
    {
        var transfer = CreatePendingTransfer();

        transfer.Approve();

        Assert.Equal(TransferStatus.Approved, transfer.Status);
        Assert.True(transfer.UpdatedAtUtc >= transfer.CreatedAtUtc);
        Assert.Null(transfer.RejectionReason);
    }

    [Fact]
    public void Approve_AlreadyApproved_DoesNothing()
    {
        var transfer = CreatePendingTransfer();
        transfer.Approve();
        var firstUpdate = transfer.UpdatedAtUtc;

        transfer.Approve(); // duplicate message

        Assert.Equal(TransferStatus.Approved, transfer.Status);
        Assert.Equal(firstUpdate, transfer.UpdatedAtUtc);
    }

    [Fact]
    public void Approve_RejectedTransfer_ThrowsInvalidOperationException()
    {
        var transfer = CreatePendingTransfer();
        transfer.Reject("Amount exceeds the limit of 10,000.");

        Assert.Throws<InvalidOperationException>(() => transfer.Approve());
        Assert.Equal(TransferStatus.Rejected, transfer.Status);
    }

    [Fact]
    public void Reject_PendingTransfer_SetsRejectedWithReason()
    {
        var transfer = CreatePendingTransfer();

        transfer.Reject("Amount exceeds the limit of 10,000.");

        Assert.Equal(TransferStatus.Rejected, transfer.Status);
        Assert.Equal("Amount exceeds the limit of 10,000.", transfer.RejectionReason);
    }

    [Fact]
    public void Reject_EmptyReason_ThrowsArgumentException()
    {
        var transfer = CreatePendingTransfer();

        var ex = Assert.Throws<ArgumentException>(() => transfer.Reject(""));

        Assert.Equal("reason", ex.ParamName);
        Assert.Equal(TransferStatus.Pending, transfer.Status);
    }

    private static Transfer CreatePendingTransfer() => new("ACC123", "ACC456", 100.00m, "EUR");
}
