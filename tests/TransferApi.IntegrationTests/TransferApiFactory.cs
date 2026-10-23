using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace TransferApi.IntegrationTests;

// Starts the real TransferApi in memory, backed by a throwaway Postgres container.
// xUnit creates one instance per test class that uses IClassFixture<TransferApiFactory>.
public class TransferApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Point the API at the test container instead of the connection string in appsettings.
        // The environment stays "Development", so Program.cs applies the migrations on startup.
        builder.UseSetting("ConnectionStrings:TransferDb", _postgres.GetConnectionString());
    }

    // Runs once before the first test in the class.
    public Task InitializeAsync() => _postgres.StartAsync();

    // Runs once after the last test in the class.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
