using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledgerly.Infrastructure.Persistence.Configurations;

internal sealed class PostingConfiguration : IEntityTypeConfiguration<PostingRecord>
{
    public void Configure(EntityTypeBuilder<PostingRecord> builder)
    {
        builder.ToTable("postings", table =>
        {
            table.HasCheckConstraint("ck_postings_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_postings_direction", "direction IN (1, 2)");
            table.HasCheckConstraint("ck_postings_sequence", "sequence >= 0");
        });
        builder.HasKey(posting => new { posting.JournalEntryId, posting.Sequence }).HasName("pk_postings");
        builder.Property(posting => posting.JournalEntryId).HasColumnName("journal_entry_id").ValueGeneratedNever();
        builder.Property(posting => posting.Sequence).HasColumnName("sequence").ValueGeneratedNever();
        builder.Property(posting => posting.AccountId).HasColumnName("account_id");
        builder.Property(posting => posting.Currency).HasColumnName("currency")
            .HasConversion(currency => currency.Code, code => Currency.FromCode(code))
            .HasMaxLength(3).IsRequired();
        builder.Property(posting => posting.Direction).HasColumnName("direction").HasConversion<int>();
        builder.Property(posting => posting.Amount).HasColumnName("amount").HasPrecision(19, 4);

        builder.HasOne<JournalEntryRecord>().WithMany(journal => journal.Postings)
            .HasForeignKey(posting => new { posting.JournalEntryId, posting.Currency })
            .HasPrincipalKey(journal => new { journal.Id, journal.Currency })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_postings_journal_currency");
        builder.HasOne<LedgerAccountRecord>().WithMany()
            .HasForeignKey(posting => new { posting.AccountId, posting.Currency })
            .HasPrincipalKey(account => new { account.Id, account.Currency })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_postings_account_currency");
    }
}
