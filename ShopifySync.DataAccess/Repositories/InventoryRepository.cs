using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ShopifySync.Common.Dtos;
using ShopifySync.DataAccess.Interfaces;

namespace ShopifySync.DataAccess.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly string _connectionString;

    public InventoryRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    private IDbConnection CreateConnection() => new SqlConnection(_connectionString);

    public Task<(bool Exists, bool IsUpdated)> UpsertInventoryAsync(ShopifyInventoryDto item, string platform, CancellationToken cancellationToken = default)
    {
        // Nota: aquí sólo dejamos el esqueleto. La implementación real deberá
        // mapear a la tabla InventarioEcommerce e InvEcom_* según tu esquema.

        const string sql = @"/* TODO: implementar INSERT/UPDATE real usando tu esquema */";

        using var connection = CreateConnection();
        // connection.Open(); // Descomentar cuando implementes SQL real.
        _ = sql;

        // Por ahora no ejecutamos nada para evitar modificar datos por accidente.
        // Devuelve que no existía ni se actualizó, pero permite compilar.
        return Task.FromResult((Exists: false, IsUpdated: false));
    }
}
