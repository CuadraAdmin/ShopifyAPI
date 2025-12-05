using ShopifySync.Common.Logging;

namespace ShopifySync.DataAccess.Interfaces;

public interface ISyncLogRepository
{
    Task SaveLogAsync(SyncLogEntry logEntry, CancellationToken cancellationToken = default);
}
