using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledgerly.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessageRecord>
{
    public void Configure(EntityTypeBuilder<OutboxMessageRecord> builder)
    {
        builder.ToTable("outbox_messages", table => table.HasCheckConstraint(
            "ck_outbox_messages_attempt_count_non_negative", "attempt_count >= 0"));
        builder.HasKey(message => message.Id).HasName("pk_outbox_messages");
        builder.Property(message => message.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(message => message.AggregateId).HasColumnName("aggregate_id");
        builder.Property(message => message.Type).HasColumnName("type").HasMaxLength(200).IsRequired();
        builder.Property(message => message.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.OccurredAtUtc).HasColumnName("occurred_at_utc");
        builder.Property(message => message.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(message => message.AttemptCount).HasColumnName("attempt_count");
        builder.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.HasIndex(message => message.OccurredAtUtc)
            .HasFilter("processed_at_utc IS NULL")
            .HasDatabaseName("ix_outbox_messages_pending");
    }
}
