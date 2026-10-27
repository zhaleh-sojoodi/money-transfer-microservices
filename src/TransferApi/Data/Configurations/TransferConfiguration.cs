using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransferApi.Domain.Entities;

namespace TransferApi.Data.Configurations;

public class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.HasKey(t => t.Id);

        // The Guid is created in the Transfer constructor, not by the database.
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.FromAccount).HasMaxLength(34).IsRequired();
        builder.Property(t => t.ToAccount).HasMaxLength(34).IsRequired();

        builder.Property(t => t.Amount).HasPrecision(18, 2);

        builder.Property(t => t.Currency).HasMaxLength(3).IsFixedLength().IsRequired();

        // Store the enum as text ('Pending') instead of an int (0).
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

        // Only set when the transfer is rejected, so the column allows NULL.
        builder.Property(t => t.RejectionReason).HasMaxLength(200);
    }
}
