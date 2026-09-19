using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Abstractions.Messaging;
using Ledgerly.Application.Ledger;
using Ledgerly.Application.Wallets;
using Ledgerly.Application.Wallets.TestDeposit;
using Ledgerly.Application.Wallets.TransferWallet;
using Ledgerly.Infrastructure.Ledger;
using Ledgerly.Infrastructure.Messaging;
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
        services.AddScoped<ILedgerAccountRepository, LedgerAccountRepository>();
        services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
        services.AddScoped<ITestDepositOperationRepository, TestDepositOperationRepository>();
        services.AddScoped<IWalletTransferRepository, WalletTransferRepository>();
        services.AddScoped<IOutboxMessageWriter, OutboxMessageWriter>();
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        if (configuration.GetValue("RabbitMq:Enabled", false))
        {
            services.AddSingleton<IIntegrationEventPublisher, RabbitMqIntegrationEventPublisher>();
        }
        else
        {
            services.AddSingleton<IIntegrationEventPublisher, LoggingIntegrationEventPublisher>();
        }
        services.AddScoped<OutboxProcessor>();
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        if (configuration.GetValue("Outbox:Enabled", true))
        {
            services.AddHostedService<OutboxPublisherWorker>();
        }
        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<LedgerlyDbContext>()
        );

        return services;
    }
}
