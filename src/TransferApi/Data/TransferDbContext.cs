using MassTransit;
using Microsoft.EntityFrameworkCore;
using TransferApi.Domain.Entities;

namespace TransferApi.Data;

public class TransferDbContext : DbContext
{
    public TransferDbContext(DbContextOptions<TransferDbContext> options) : base(options) { }

    public DbSet<Transfer> Transfers => Set<Transfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration<T> in this project (e.g. TransferConfiguration).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransferDbContext).Assembly);

        // MassTransit outbox tables: messages are saved here in the same transaction as the transfer.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
