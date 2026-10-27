using Microsoft.EntityFrameworkCore;
using NotificationWorker.Data;
using Testcontainers.PostgreSql;

namespace NotificationWorker.IntegrationTests;

// One throwaway Postgres container per test class, with the real migrations applied
// (so the unique index on transfer_id exists exactly as in production).
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public NotificationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}
