using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledgerly.Infrastructure.Persistence.Configurations;

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccountRecord>
{
    public void Configure(EntityTypeBuilder<LedgerAccountRecord> builder)
    {
        builder.ToTable("ledger_accounts", table => table.HasCheckConstraint(
            "ck_ledger_accounts_type_wallet",
            "(type = 1 AND wallet_id IS NULL) OR (type = 2 AND wallet_id IS NOT NULL)"));
        builder.HasKey(account => account.Id).HasName("pk_ledger_accounts");
        builder.HasAlternateKey(account => new { account.Id, account.Currency })
            .HasName("ak_ledger_accounts_id_currency");
        builder.Property(account => account.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(account => account.WalletId).HasColumnName("wallet_id");
        builder.Property(account => account.Type).HasColumnName("type").HasConversion<int>();
        builder.Property(account => account.Currency).HasColumnName("currency")
            .HasConversion(currency => currency.Code, code => Currency.FromCode(code))
            .HasMaxLength(3).IsRequired();
        builder.Property(account => account.CreatedAtUtc).HasColumnName("created_at_utc");

        builder.HasOne<Wallet>().WithMany()
            .HasForeignKey(account => new { account.WalletId, account.Currency })
            .HasPrincipalKey(wallet => new { wallet.Id, wallet.Currency })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ledger_accounts_wallet_currency");
        builder.HasIndex(account => account.WalletId).IsUnique()
            .HasFilter("wallet_id IS NOT NULL").HasDatabaseName("ux_ledger_accounts_wallet_id");
        builder.HasIndex(account => account.Currency).IsUnique()
            .HasFilter("wallet_id IS NULL").HasDatabaseName("ux_ledger_accounts_test_funding_currency");
    }
}
