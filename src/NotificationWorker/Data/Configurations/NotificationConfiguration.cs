using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationWorker.Domain.Entities;

namespace NotificationWorker.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);

        // The Guid is created in code, not by the database.
        builder.Property(n => n.Id).ValueGeneratedNever();

        // One notification per transfer: the database rejects a second row for the same transfer,
        // which protects against duplicate messages (at-least-once delivery).
        builder.HasIndex(n => n.TransferId).IsUnique();

        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.Recipient).HasMaxLength(34).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(500).IsRequired();
    }
}
