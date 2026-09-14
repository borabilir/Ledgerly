using Ledgerly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerly.IntegrationTests.Infrastructure;

// Integration testlerinde API'yi bizim yerimize ayağa kaldırır ve gönderdiğimiz HTTP isteklerini ayrı PostgreSQL test veritabanına kadar gerçek akıştan geçirir.
public sealed class LedgerlyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string SettingsFileName = "appsettings.IntegrationTests.json";
    private const string ConnectionStringEnvironmentVariable =
        "LEDGERLY_TEST_DB_CONNECTION_STRING";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(SettingsFileName, optional: false)
            .Build();

        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable
        ) ?? configuration.GetConnectionString("Database");

        builder.UseSetting("ConnectionStrings:Database", connectionString);
    }

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();

        await dbContext.Database.MigrateAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public async Task DeleteWalletsAsync(Guid ownerId)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();

        await dbContext.Wallets
            .Where(wallet => wallet.OwnerId == ownerId)
            .ExecuteDeleteAsync();
    }

    public async Task<bool> WalletExistsAsync(Guid walletId)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();

        return await dbContext.Wallets
            .AsNoTracking()
            .AnyAsync(wallet => wallet.Id == walletId);
    }

    public async Task<int> CountWalletsAsync(Guid ownerId)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();

        return await dbContext.Wallets
            .AsNoTracking()
            .CountAsync(wallet => wallet.OwnerId == ownerId);
    }
}
