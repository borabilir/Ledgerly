using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerly.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' was not found."
            );

        services.AddDbContext<LedgerlyDbContext>(options =>
            options.UseNpgsql(connectionString)
        );

        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<LedgerlyDbContext>()
        );

        return services;
    }
}
