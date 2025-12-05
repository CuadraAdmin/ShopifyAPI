using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ShopifySync.Common.Logging;
using ShopifySync.DataAccess.Interfaces;

namespace ShopifySync.DataAccess.Repositories;

public class SyncLogRepository : ISyncLogRepository
{
    private readonly string _connectionString;

    public SyncLogRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public Task SaveLogAsync(SyncLogEntry logEntry, CancellationToken cancellationToken = default)
    {
        const string sql = @"/* TODO: implementar INSERT en tu tabla de logs */";

        using var connection = CreateConnection();
        // connection.Open(); // Descomentar cuando implementes SQL real.

        // Igual que en InventoryRepository, dejamos el punto de expansión pero
        // no ejecutamos SQL real hasta que definas el esquema exacto.
        _ = sql;

        return Task.CompletedTask;
    }
}
