using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledgerly.Infrastructure.Persistence.Configurations;

internal sealed class WalletTransferConfiguration : IEntityTypeConfiguration<WalletTransferRecord>
{
    internal const string IdempotencyUniqueIndexName = "ux_wallet_transfers_source_wallet_id_idempotency_key";

    public void Configure(EntityTypeBuilder<WalletTransferRecord> builder)
    {
        builder.ToTable("wallet_transfers", table => table.HasCheckConstraint(
            "ck_wallet_transfers_amount_positive", "amount > 0"));
        builder.HasKey(transfer => transfer.Id).HasName("pk_wallet_transfers");
        builder.Property(transfer => transfer.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(transfer => transfer.SourceWalletId).HasColumnName("source_wallet_id");
        builder.Property(transfer => transfer.DestinationWalletId).HasColumnName("destination_wallet_id");
        builder.Property(transfer => transfer.IdempotencyKey).HasColumnName("idempotency_key")
            .HasMaxLength(128).IsRequired();
        builder.Property(transfer => transfer.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(transfer => transfer.CurrencyCode).HasColumnName("currency")
            .HasMaxLength(3).IsRequired();
        builder.Property(transfer => transfer.JournalEntryId).HasColumnName("journal_entry_id");
        builder.Property(transfer => transfer.SourceBalance).HasColumnName("source_balance").HasPrecision(19, 4);
        builder.Property(transfer => transfer.DestinationBalance).HasColumnName("destination_balance").HasPrecision(19, 4);
        builder.Property(transfer => transfer.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(transfer => new { transfer.SourceWalletId, transfer.IdempotencyKey })
            .IsUnique().HasDatabaseName(IdempotencyUniqueIndexName);
        builder.HasIndex(transfer => transfer.JournalEntryId).IsUnique()
            .HasDatabaseName("ux_wallet_transfers_journal_entry_id");
        builder.HasOne<Wallet>().WithMany().HasForeignKey(transfer => transfer.SourceWalletId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_transfers_source_wallet");
        builder.HasOne<Wallet>().WithMany().HasForeignKey(transfer => transfer.DestinationWalletId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_transfers_destination_wallet");
        builder.HasOne<JournalEntryRecord>().WithMany().HasForeignKey(transfer => transfer.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_wallet_transfers_journal");
    }
}
