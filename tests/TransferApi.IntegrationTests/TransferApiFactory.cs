using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using TransferApi.Data;

namespace TransferApi.IntegrationTests;

// Starts the real TransferApi in memory, backed by throwaway Postgres and RabbitMQ containers.
// xUnit creates one instance per test class that uses IClassFixture<TransferApiFactory>.
public class TransferApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.1").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Point the API at the test containers instead of the connection strings in appsettings.
        builder.UseSetting("ConnectionStrings:TransferDb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbitMq.GetConnectionString());
    }

    // Runs once before the first test in the class. Both containers start in parallel.
    // Program.cs no longer migrates, so the real migrations are applied here, before the API starts.
    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        await using var db = new TransferDbContext(new DbContextOptionsBuilder<TransferDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);
        await db.Database.MigrateAsync();
    }

    // Runs once after the last test in the class.
    // Stop the API first, so it doesn't try to reach containers that are already gone.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
