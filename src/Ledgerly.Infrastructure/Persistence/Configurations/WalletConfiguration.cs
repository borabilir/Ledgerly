using Ledgerly.Domain.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledgerly.Infrastructure.Persistence.Configurations;

internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("wallets");

        builder.HasKey(wallet => wallet.Id)
            .HasName("pk_wallets");

        builder.Property(wallet => wallet.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(wallet => wallet.OwnerId)
            .HasColumnName("owner_id")
            .IsRequired();

        builder.Property(wallet => wallet.Currency)
            .HasColumnName("currency")
            .HasConversion(
                currency => currency.Code,
                code => Currency.FromCode(code)
            )
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(wallet => wallet.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(wallet => wallet.Balance)
            .HasColumnName("balance")
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(wallet => wallet.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(wallet => new { wallet.OwnerId, wallet.Currency })
            .IsUnique()
            .HasDatabaseName("ux_wallets_owner_id_currency");
    }
}
