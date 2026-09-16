using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledgerly.Infrastructure.Persistence.Configurations;

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntryRecord>
{
    public void Configure(EntityTypeBuilder<JournalEntryRecord> builder)
    {
        builder.ToTable("journal_entries");
        builder.HasKey(journal => journal.Id).HasName("pk_journal_entries");
        builder.HasAlternateKey(journal => new { journal.Id, journal.Currency })
            .HasName("ak_journal_entries_id_currency");
        builder.Property(journal => journal.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(journal => journal.Currency).HasColumnName("currency")
            .HasConversion(currency => currency.Code, code => Currency.FromCode(code))
            .HasMaxLength(3).IsRequired();
        builder.Property(journal => journal.CreatedAtUtc).HasColumnName("created_at_utc");
    }
}
