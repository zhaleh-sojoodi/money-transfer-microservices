using Contracts;
using MassTransit;
using TransferApi.Domain.Entities;
using TransferApi.Domain.Requests;
using TransferApi.Repositories.IRepositories;
using TransferApi.Services.IServices;

namespace TransferApi.Services;

public class TransferService : ITransferService
{
    private readonly ITransferRepository _repository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<TransferService> _logger;

    public TransferService(ITransferRepository repository, IPublishEndpoint publishEndpoint, ILogger<TransferService> logger)
    {
        _repository = repository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Transfer> CreateAsync(CreateTransferRequest request, CancellationToken cancellationToken = default)
    {
        // Throws ArgumentException if a business rule fails; the controller turns that into a 400.
        var transfer = new Transfer(request.FromAccount, request.ToAccount, request.Amount, request.Currency);

        await _repository.AddAsync(transfer, cancellationToken);

        // With the outbox, Publish does not send anything yet: it adds the message to the DbContext.
        // It must come BEFORE SaveChangesAsync so the transfer and the message are committed together.
        await _publishEndpoint.Publish(new TransferRequested(
            transfer.Id,
            transfer.FromAccount,
            transfer.ToAccount,
            transfer.Amount,
            transfer.Currency,
            transfer.CreatedAtUtc), cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Transfer {TransferId} created with status {Status}", transfer.Id, transfer.Status);

        return transfer;
    }

    public async Task<Transfer> ApproveAsync(Guid transferId, CancellationToken cancellationToken = default) 
    {
        var transfer = await _repository.GetByIdAsync(transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer {transferId} not found.");
        transfer.Approve();

        await _repository.SaveChangesAsync(cancellationToken);
        return transfer;
    }

    public async Task<Transfer> RejectAsync(Guid transferId, string reason, CancellationToken cancellationToken = default) 
    {
        var transfer = await _repository.GetByIdAsync(transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer {transferId} not found.");
        transfer.Reject(reason);

        await _repository.SaveChangesAsync(cancellationToken);
        return transfer;   
    }

    public Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _repository.GetByIdAsync(id, cancellationToken);
    }
}
