using Microsoft.EntityFrameworkCore;
using TransferApi.Data;
using TransferApi.Domain.Entities;
using TransferApi.Repositories.IRepositories;

namespace TransferApi.Repositories;

public class TransferRepository : ITransferRepository
{
    private readonly TransferDbContext _db;

    public TransferRepository(TransferDbContext db)
    {
        _db = db;
    }

    public async Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Transfers.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task AddAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        await _db.Transfers.AddAsync(transfer, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}
