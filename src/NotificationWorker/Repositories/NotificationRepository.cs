using Microsoft.EntityFrameworkCore;
using Npgsql;
using NotificationWorker.Data;
using NotificationWorker.Domain.Entities;
using NotificationWorker.Repositories.IRepositories;

namespace NotificationWorker.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly NotificationDbContext _dbContext;

    public NotificationRepository(NotificationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Notification?> GetByTransferIdAsync(Guid transferId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications.FirstOrDefaultAsync(n => n.TransferId == transferId, cancellationToken);
    }

    public async Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await _dbContext.Notifications.AddAsync(notification, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _dbContext.Entry(notification).State = EntityState.Detached;
            return false;
        }
    }
}
