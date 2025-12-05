using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ShopifySync.Common.Logging;
using ShopifySync.DataAccess.Interfaces;

namespace ShopifySync.DataAccess.Repositories;

public class SyncLogRepository : ISyncLogRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SyncLogRepository> _logger;
    private long? _currentSyncId;

    public SyncLogRepository(string connectionString, ILogger<SyncLogRepository> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    public void SetCurrentSyncId(long syncId)
    {
        _currentSyncId = syncId;
    }

    public async Task SaveLogAsync(SyncLogEntry logEntry, CancellationToken cancellationToken = default)
    {
        try
        {
            const string insertLogSql = @"
                INSERT INTO dbo.ShopifyLog (
                    Synd_Id,
                    Log_Identificador,
                    Log_Tipo,
                    Log_Mensaje,
                    Log_Detalle,
                    Log_Fecha
                ) VALUES (
                    @SyncId,
                    @Identifier,
                    @Type,
                    @Message,
                    @DetailJson,
                    @Timestamp
                )";

            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await connection.ExecuteAsync(
                insertLogSql,
                new
                {
                    SyncId = _currentSyncId,
                    Identifier = logEntry.Identifier?.Substring(0, Math.Min(50, logEntry.Identifier?.Length ?? 0)),
                    Type = logEntry.Type?.Substring(0, Math.Min(50, logEntry.Type?.Length ?? 0)),
                    Message = logEntry.Message?.Substring(0, Math.Min(50, logEntry.Message?.Length ?? 0)),
                    DetailJson = logEntry.DetailJson?.Substring(0, Math.Min(50, logEntry.DetailJson?.Length ?? 0)),
                    Timestamp = logEntry.Timestamp
                },
                commandTimeout: 30
            );
        }
        catch (Exception ex)
        {
            // No lanzamos la excepción para evitar que un error de logging detenga la sincronización
            _logger.LogError(ex, "[SyncLogRepository] Error guardando log: {Type} - {Message}",
                logEntry.Type, logEntry.Message);
        }
    }

    public async Task<long> CreateSyncRecordAsync(string syncType, CancellationToken cancellationToken = default)
    {
        const string insertSyncSql = @"
            INSERT INTO dbo.ShopifyControl (
                Sync_Tipo,
                Sync_FechaInicio,
                Sync_Estado,
                Sync_Consultados,
                Sync_Insertados,
                Sync_Actualizados,
                Sync_Fallidos
            ) VALUES (
                @SyncType,
                GETDATE(),
                'EnProceso',
                0,
                0,
                0,
                0
            );
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var syncId = await connection.ExecuteScalarAsync<long>(
            insertSyncSql,
            new { SyncType = syncType },
            commandTimeout: 30
        );

        _currentSyncId = syncId;
        return syncId;
    }

    public async Task UpdateSyncRecordAsync(
        long syncId,
        string status,
        int consultados,
        int insertados,
        int actualizados,
        int fallidos,
        string? mensaje = null,
        CancellationToken cancellationToken = default)
    {
        const string updateSyncSql = @"
            UPDATE dbo.ShopifyControl
            SET 
                Sync_FechaFin = GETDATE(),
                Sync_Estado = @Status,
                Sync_Consultados = @Consultados,
                Sync_Insertados = @Insertados,
                Sync_Actualizados = @Actualizados,
                Sync_Fallidos = @Fallidos,
                Sync_Mensaje = @Mensaje
            WHERE Sync_Id = @SyncId";

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            updateSyncSql,
            new
            {
                SyncId = syncId,
                Status = status,
                Consultados = consultados,
                Insertados = insertados,
                Actualizados = actualizados,
                Fallidos = fallidos,
                Mensaje = mensaje?.Substring(0, Math.Min(50, mensaje?.Length ?? 0))
            },
            commandTimeout: 30
        );
    }
}