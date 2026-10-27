using Microsoft.EntityFrameworkCore;
using NotificationWorker.Domain.Entities;
using NotificationWorker.Domain.Enums;
using NotificationWorker.Repositories;

namespace NotificationWorker.IntegrationTests;

public class NotificationRepositoryTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _db;

    public NotificationRepositoryTests(PostgresFixture db)
    {
        _db = db;
    }

    // The race: two copies of a message both pass the "already exists?" check, then both insert.
    // Two separate DbContexts play the two copies; the unique index must let only one through.
    [Fact]
    public async Task TryAddAsync_SecondInsertForSameTransfer_ReturnsFalse()
    {
        var transferId = Guid.NewGuid();
        await using var firstContext = _db.CreateDbContext();
        await using var secondContext = _db.CreateDbContext();

        var firstInserted = await new NotificationRepository(firstContext)
            .TryAddAsync(new Notification(transferId, NotificationType.TransferApproved, "NL01BANK0001", "first copy"));
        var secondInserted = await new NotificationRepository(secondContext)
            .TryAddAsync(new Notification(transferId, NotificationType.TransferApproved, "NL01BANK0001", "second copy"));

        Assert.True(firstInserted);
        Assert.False(secondInserted);

        await using var check = _db.CreateDbContext();
        var saved = await check.Notifications.SingleAsync(n => n.TransferId == transferId);
        Assert.Equal("first copy", saved.Message);
    }
}
