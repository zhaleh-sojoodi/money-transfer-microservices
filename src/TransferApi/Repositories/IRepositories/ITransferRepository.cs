using TransferApi.Domain.Entities;

namespace TransferApi.Repositories.IRepositories;

public interface ITransferRepository
{
    Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Transfer transfer, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
