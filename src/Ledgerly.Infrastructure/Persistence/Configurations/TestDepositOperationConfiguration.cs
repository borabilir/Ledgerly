using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledgerly.Infrastructure.Persistence.Configurations;

internal sealed class TestDepositOperationConfiguration : IEntityTypeConfiguration<TestDepositOperationRecord>
{
    internal const string PrimaryKeyName = "pk_test_deposit_operations";

    public void Configure(EntityTypeBuilder<TestDepositOperationRecord> builder)
    {
        builder.ToTable("test_deposit_operations", table => table.HasCheckConstraint(
            "ck_test_deposit_operations_amount_positive", "amount > 0"));
        builder.HasKey(operation => new { operation.WalletId, operation.Key }).HasName(PrimaryKeyName);
        builder.Property(operation => operation.WalletId).HasColumnName("wallet_id");
        builder.Property(operation => operation.Key).HasColumnName("key").HasMaxLength(128).IsRequired();
        builder.Property(operation => operation.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(operation => operation.JournalEntryId).HasColumnName("journal_entry_id");
        builder.Property(operation => operation.CurrencyCode).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(operation => operation.Balance).HasColumnName("balance").HasPrecision(19, 4);
        builder.HasOne<Wallet>().WithMany().HasForeignKey(operation => operation.WalletId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_test_deposit_operations_wallet");
        builder.HasOne<JournalEntryRecord>().WithMany().HasForeignKey(operation => operation.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_test_deposit_operations_journal");
        builder.HasIndex(operation => operation.JournalEntryId).IsUnique()
            .HasDatabaseName("ux_test_deposit_operations_journal_entry_id");
    }
}
