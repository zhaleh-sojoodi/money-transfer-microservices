using TransferApi.Domain.Entities;
using TransferApi.Domain.Requests;

namespace TransferApi.Services.IServices;

public interface ITransferService
{
    Task<Transfer> CreateAsync(CreateTransferRequest request, CancellationToken cancellationToken = default);
    Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
