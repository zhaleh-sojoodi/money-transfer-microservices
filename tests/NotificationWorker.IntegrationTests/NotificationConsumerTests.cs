using Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationWorker.Consumers;
using NotificationWorker.Data;
using NotificationWorker.Domain.Enums;
using NotificationWorker.Repositories;
using NotificationWorker.Repositories.IRepositories;
using NotificationWorker.Services;
using NotificationWorker.Services.IServices;

namespace NotificationWorker.IntegrationTests;

// Real Postgres (Testcontainers) + in-memory bus (MassTransit test harness):
// the consumers, service, repository and database run for real; only RabbitMQ is replaced.
public class NotificationConsumerTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _db;
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public NotificationConsumerTests(PostgresFixture db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<NotificationDbContext>(o => o.UseNpgsql(_db.ConnectionString).UseSnakeCaseNamingConvention())
            .AddScoped<INotificationRepository, NotificationRepository>()
            .AddScoped<INotificationServices, NotificationService>()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<TransferApprovedConsumer>();
                x.AddConsumer<TransferRejectedConsumer>();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task TransferApproved_SavesApprovedNotification()
    {
        var message = new TransferApproved(Guid.NewGuid(), "NL01BANK0001", "NL02BANK0002", 50.00m, "EUR", DateTime.UtcNow);

        await _harness.Bus.Publish(message);
        await _harness.InactivityTask; // wait until the bus has finished processing

        await using var db = _db.CreateDbContext();
        var notification = await db.Notifications.SingleAsync(n => n.TransferId == message.TransferId);
        Assert.Equal(NotificationType.TransferApproved, notification.Type);
        Assert.Equal("NL01BANK0001", notification.Recipient);
        Assert.Equal("Your transfer of 50.00 EUR to NL02BANK0002 was approved.", notification.Message);
    }

    [Fact]
    public async Task TransferRejected_SavesNotificationWithReason()
    {
        var message = new TransferRejected(Guid.NewGuid(), "NL01BANK0001", "NL02BANK0002", 20_000.00m, "EUR",
            "Amount exceeds the limit of 10,000.", DateTime.UtcNow);

        await _harness.Bus.Publish(message);
        await _harness.InactivityTask;

        await using var db = _db.CreateDbContext();
        var notification = await db.Notifications.SingleAsync(n => n.TransferId == message.TransferId);
        Assert.Equal(NotificationType.TransferRejected, notification.Type);
        Assert.Equal(
            "Your transfer of 20000.00 EUR to NL02BANK0002 was rejected: Amount exceeds the limit of 10,000.",
            notification.Message);
    }

    [Fact]
    public async Task SameMessageTwice_SavesOnlyOneNotification()
    {
        var message = new TransferApproved(Guid.NewGuid(), "NL01BANK0001", "NL02BANK0002", 50.00m, "EUR", DateTime.UtcNow);

        // At-least-once delivery: the same event can arrive twice.
        await _harness.Bus.Publish(message);
        await _harness.Bus.Publish(message);
        await _harness.InactivityTask;

        await using var db = _db.CreateDbContext();
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.TransferId == message.TransferId));

        // Both copies were handled successfully: a duplicate is "already done", not an error.
        Assert.False(await _harness.Consumed.Any<TransferApproved>(x => x.Exception != null));
    }
}
