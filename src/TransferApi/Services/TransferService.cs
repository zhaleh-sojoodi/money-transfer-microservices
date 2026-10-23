using TransferApi.Domain.Entities;
using TransferApi.Domain.Requests;
using TransferApi.Repositories.IRepositories;
using TransferApi.Services.IServices;

namespace TransferApi.Services;

public class TransferService : ITransferService
{
    private readonly ITransferRepository _repository;
    private readonly ILogger<TransferService> _logger;

    public TransferService(ITransferRepository repository, ILogger<TransferService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Transfer> CreateAsync(CreateTransferRequest request, CancellationToken cancellationToken = default)
    {
        // Throws ArgumentException if a business rule fails; the controller turns that into a 400.
        var transfer = new Transfer(request.FromAccount, request.ToAccount, request.Amount, request.Currency);

        await _repository.AddAsync(transfer, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        // Later: publish TransferRequested here (via the MassTransit outbox).

        _logger.LogInformation("Transfer {TransferId} created with status {Status}", transfer.Id, transfer.Status);

        return transfer;
    }

    public Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _repository.GetByIdAsync(id, cancellationToken);
    }
}
