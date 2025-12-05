using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ShopifySync.Common.Dtos;
using ShopifySync.DataAccess.Interfaces;

namespace ShopifySync.DataAccess.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly string _connectionString;
    private readonly ILogger<InventoryRepository> _logger;

    public InventoryRepository(string connectionString, ILogger<InventoryRepository> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<(bool Exists, bool IsUpdated)> UpsertInventoryAsync(ShopifyInventoryDto item, string platform, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            // PASO 1: Buscar Prod_Id por EAN si existe
            int? prodId = null;
            if (!string.IsNullOrWhiteSpace(item.Ean))
            {
                const string findProdIdSql = @"
                    SELECT TOP 1 Prod_Id 
                    FROM Productos 
                    WHERE Prod_EAN = @Ean OR Prod_CodigoBarras = @Ean";

                prodId = await connection.QueryFirstOrDefaultAsync<int?>(
                    findProdIdSql,
                    new { Ean = item.Ean },
                    commandTimeout: 30
                );

                if (prodId.HasValue)
                {
                    item.ProdId = prodId.Value;
                }
                else
                {
                    _logger.LogWarning("[InventoryRepository] Producto no encontrado en BD interna para EAN: {Ean}, SKU: {Sku}",
                        item.Ean, item.Sku);
                }
            }

            // PASO 2: Verificar si ya existe el registro
            const string checkExistsSql = @"
                SELECT InvEcom_Id 
                FROM InventarioEcommerce 
                WHERE InvEcom_Plataforma = @Platform
                  AND InvEcom_ProdId_Plataforma = @ProductId
                  AND ISNULL(InvEcome_Locacion, '') = ISNULL(@Location, '')";

            var existingId = await connection.QueryFirstOrDefaultAsync<long?>(
                checkExistsSql,
                new
                {
                    Platform = platform,
                    ProductId = item.VariantId ?? item.ProductId,
                    Location = item.LocationName
                },
                commandTimeout: 30
            );

            bool exists = existingId.HasValue;
            bool isUpdated = false;

            if (exists)
            {
                // PASO 3A: UPDATE
                const string updateSql = @"
                    UPDATE InventarioEcommerce
                    SET 
                        InvEcom_SKU = @Sku,
                        InvEcom_EAN = @Ean,
                        InvEcom_ProdId_Plataforma = @ProductId,
                        Prod_Id = @ProdId,
                        InvEcom_Disponible = @Available,
                        InvEcom_EnTransito = @Incoming,
                        InvEcom_Reservado = @Reserved,
                        InvEcom_Danado = @Damaged,
                        InvEcom_Total = @OnHand,
                        InvEcome_PrecioVenta = @Price,
                        InvEcom_PrecioRegular = @CompareAtPrice,
                        InvEcome_Locacion = @Location,
                        InvEcom_FechaModificacion = GETDATE(),
                        InvEcom_ModificadoPor = 'SystemSync'
                    WHERE InvEcom_Id = @Id";

                var affectedRows = await connection.ExecuteAsync(
                    updateSql,
                    new
                    {
                        Id = existingId.Value,
                        Sku = item.Sku,
                        Ean = item.Ean,
                        ProductId = item.VariantId ?? item.ProductId,
                        ProdId = item.ProdId,
                        Available = item.Available,
                        Incoming = item.Incoming,
                        Reserved = item.Reserved,
                        Damaged = item.Damaged,
                        OnHand = item.OnHand,
                        Price = item.Price,
                        CompareAtPrice = item.CompareAtPrice,
                        Location = item.LocationName
                    },
                    commandTimeout: 30
                );

                isUpdated = affectedRows > 0;
            }
            else
            {
                // PASO 3B: INSERT
                const string insertSql = @"
                    INSERT INTO InventarioEcommerce (
                        InvEcom_Plataforma,
                        InvEcom_SKU,
                        InvEcom_EAN,
                        InvEcom_ProdId_Plataforma,
                        Prod_Id,
                        InvEcom_Disponible,
                        InvEcom_EnTransito,
                        InvEcom_Reservado,
                        InvEcom_Danado,
                        InvEcom_Total,
                        InvEcome_PrecioVenta,
                        InvEcom_PrecioRegular,
                        InvEcome_Locacion,
                        InvEcom_CreadoPor,
                        InvEcom_FechaCreacion
                    ) VALUES (
                        @Platform,
                        @Sku,
                        @Ean,
                        @ProductId,
                        @ProdId,
                        @Available,
                        @Incoming,
                        @Reserved,
                        @Damaged,
                        @OnHand,
                        @Price,
                        @CompareAtPrice,
                        @Location,
                        'SystemSync',
                        GETDATE()
                    )";

                await connection.ExecuteAsync(
                    insertSql,
                    new
                    {
                        Platform = platform,
                        Sku = item.Sku,
                        Ean = item.Ean,
                        ProductId = item.VariantId ?? item.ProductId,
                        ProdId = item.ProdId,
                        Available = item.Available,
                        Incoming = item.Incoming,
                        Reserved = item.Reserved,
                        Damaged = item.Damaged,
                        OnHand = item.OnHand,
                        Price = item.Price,
                        CompareAtPrice = item.CompareAtPrice,
                        Location = item.LocationName
                    },
                    commandTimeout: 30
                );

                isUpdated = true; // Consideramos "updated" porque se creó
            }

            return (exists, isUpdated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[InventoryRepository] Error en UpsertInventoryAsync para SKU: {Sku}, EAN: {Ean}",
                item.Sku, item.Ean);
            throw;
        }
    }
}