using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Infrastructure;
using Ledgerly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerly.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private const string SettingsFileName = "appsettings.IntegrationTests.json";
    private const string ConnectionStringEnvironmentVariable =
        "LEDGERLY_TEST_DB_CONNECTION_STRING";

    public static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    public PostgresFixture()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(SettingsFileName, optional: false)
            .Build();

        var connectionStringOverride = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable
        );

        if (!string.IsNullOrWhiteSpace(connectionStringOverride))
        {
            configuration["ConnectionStrings:Database"] = connectionStringOverride;
        }

        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);
        services.AddScoped<CreateWalletHandler>();
        services.AddSingleton<TimeProvider>(new StubTimeProvider(FixedUtcNow));

        Services = services.BuildServiceProvider(validateScopes: true);
    }

    public ServiceProvider Services { get; }

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();

        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public StubTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection
    : ICollectionFixture<PostgresFixture>, ICollectionFixture<LedgerlyApiFactory>
{
    public const string Name = "PostgreSQL";
}
