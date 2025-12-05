using ShopifySync.Common.Logging;

namespace ShopifySync.DataAccess.Interfaces;

public interface ISyncLogRepository
{
    Task SaveLogAsync(SyncLogEntry logEntry, CancellationToken cancellationToken = default);

    Task<long> CreateSyncRecordAsync(string syncType, CancellationToken cancellationToken = default);

    Task UpdateSyncRecordAsync(
        long syncId,
        string status,
        int consultados,
        int insertados,
        int actualizados,
        int fallidos,
        string? mensaje = null,
        CancellationToken cancellationToken = default);

    void SetCurrentSyncId(long syncId);
}