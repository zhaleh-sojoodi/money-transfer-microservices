using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

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
        // The environment stays "Development", so Program.cs applies the migrations on startup.
        builder.UseSetting("ConnectionStrings:TransferDb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbitMq.GetConnectionString());
    }

    // Runs once before the first test in the class. Both containers start in parallel.
    public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

    // Runs once after the last test in the class.
    // Stop the API first, so it doesn't try to reach containers that are already gone.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
