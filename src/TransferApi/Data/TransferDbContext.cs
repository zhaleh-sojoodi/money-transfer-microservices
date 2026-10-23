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
    }
}
