using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShopifySync.Common.Dtos;
using ShopifySync.Common.Logging;
using ShopifySync.DataAccess.Interfaces;
using ShopifySync.Integration.Interfaces;

namespace ShopifySync.Core.Services;

public class InventorySyncService
{
    private readonly IShopifyInventoryClient _shopifyClient;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly ILogger<InventorySyncService> _logger;
    private readonly IConfiguration _configuration;

    public InventorySyncService(
        IShopifyInventoryClient shopifyClient,
        IInventoryRepository inventoryRepository,
        ISyncLogRepository syncLogRepository,
        ILogger<InventorySyncService> logger,
        IConfiguration configuration)
    {
        _shopifyClient = shopifyClient;
        _inventoryRepository = inventoryRepository;
        _syncLogRepository = syncLogRepository;
        _logger = logger;
        _configuration = configuration;
    }

    private IEnumerable<string> GetStores()
    {
        // Puedes definir en appsettings algo como: "Shopify:Stores": ["Store1", "Store2"]
        var stores = _configuration.GetSection("Shopify:Stores").Get<string[]>();
        return stores?.Length > 0 ? stores : new[] { "Default" };
    }

    public async Task RunFullInventorySyncAsync(CancellationToken cancellationToken = default)
    {
        var summary = new SyncSummary
        {
            SyncType = "FullInventory",
            StartTime = DateTime.UtcNow
        };

        try
        {
            foreach (var store in GetStores())
            {
                _logger.LogInformation("[FullInventory] Iniciando extracción de inventario para tienda {Store}", store);
                var items = await _shopifyClient.GetFullInventoryAsync(store, cancellationToken).ConfigureAwait(false);

                summary.TotalItems += items.Count;

                foreach (var item in items)
                {
                    try
                    {
                        // Aquí deberías hacer el matching por EAN contra tu BD interna antes de persistir.
                        var result = await _inventoryRepository
                            .UpsertInventoryAsync(item, "Shopify", cancellationToken)
                            .ConfigureAwait(false);

                        if (!result.Exists)
                            summary.Inserted++;
                        else if (result.IsUpdated)
                            summary.Updated++;

                        var successLog = new SyncLogEntry
                        {
                            Type = "Success",
                            Identifier = item.Sku ?? item.Ean ?? item.ProductId ?? string.Empty,
                            Message = "Producto sincronizado exitosamente"
                        };
                        await _syncLogRepository.SaveLogAsync(successLog, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        summary.Failed++;
                        _logger.LogError(ex, "Error al sincronizar item Shopify: {Sku}", item.Sku);

                        var errorLog = new SyncLogEntry
                        {
                            Type = "Error",
                            Identifier = item.Sku ?? item.Ean ?? item.ProductId ?? string.Empty,
                            Message = "Error al actualizar inventario",
                            DetailJson = ex.ToString()
                        };
                        await _syncLogRepository.SaveLogAsync(errorLog, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            summary.Status = summary.Failed > 0 ? "CompletadoConErrores" : "Completado";
        }
        catch (Exception ex)
        {
            summary.Status = "Fallido";
            summary.Message = ex.Message;
            _logger.LogError(ex, "Error general en sincronización full de inventario Shopify");
        }
        finally
        {
            summary.EndTime = DateTime.UtcNow;
        }
    }

    public async Task RunIncrementalInventorySyncAsync(CancellationToken cancellationToken = default)
    {
        var fechaInicio = DateTime.UtcNow.Date.AddDays(-1);
        var fechaFin = fechaInicio.AddDays(1).AddTicks(-1);

        var summary = new SyncSummary
        {
            SyncType = "IncrementalInventory",
            StartTime = DateTime.UtcNow
        };

        try
        {
            foreach (var store in GetStores())
            {
                _logger.LogInformation("[IncrementalInventory] Extrayendo inventario modificado entre {From} y {To} para tienda {Store}", fechaInicio, fechaFin, store);
                var items = await _shopifyClient
                    .GetInventoryIncrementalAsync(store, fechaInicio, fechaFin, cancellationToken)
                    .ConfigureAwait(false);

                summary.TotalItems += items.Count;

                foreach (var item in items)
                {
                    try
                    {
                        var result = await _inventoryRepository
                            .UpsertInventoryAsync(item, "Shopify", cancellationToken)
                            .ConfigureAwait(false);

                        if (!result.Exists)
                            summary.Inserted++;
                        else if (result.IsUpdated)
                            summary.Updated++;
                    }
                    catch (Exception ex)
                    {
                        summary.Failed++;
                        _logger.LogError(ex, "Error al sincronizar item incremental Shopify: {Sku}", item.Sku);
                    }
                }
            }

            summary.Status = summary.Failed > 0 ? "CompletadoConErrores" : "Completado";
        }
        catch (Exception ex)
        {
            summary.Status = "Fallido";
            summary.Message = ex.Message;
            _logger.LogError(ex, "Error general en sincronización incremental de inventario Shopify");
        }
        finally
        {
            summary.EndTime = DateTime.UtcNow;
        }
    }

    public async Task RunPriceUpdateAsync(CancellationToken cancellationToken = default)
    {
        var summary = new SyncSummary
        {
            SyncType = "PriceUpdate",
            StartTime = DateTime.UtcNow
        };

        try
        {
            foreach (var store in GetStores())
            {
                _logger.LogInformation("[PriceUpdate] Extrayendo precios para tienda {Store}", store);
                var items = await _shopifyClient.GetPricesAsync(store, cancellationToken).ConfigureAwait(false);

                summary.TotalItems += items.Count;

                foreach (var item in items)
                {
                    try
                    {
                        // Aquí podrías implementar un UPDATE masivo por ProductId si lo deseas.
                        var result = await _inventoryRepository
                            .UpsertInventoryAsync(item, "Shopify", cancellationToken)
                            .ConfigureAwait(false);

                        if (!result.Exists)
                            summary.Inserted++;
                        else if (result.IsUpdated)
                            summary.Updated++;
                    }
                    catch (Exception ex)
                    {
                        summary.Failed++;
                        _logger.LogError(ex, "Error al actualizar precios Shopify para SKU {Sku}", item.Sku);
                    }
                }
            }

            summary.Status = summary.Failed > 0 ? "CompletadoConErrores" : "Completado";
        }
        catch (Exception ex)
        {
            summary.Status = "Fallido";
            summary.Message = ex.Message;
            _logger.LogError(ex, "Error general en actualización de precios Shopify");
        }
        finally
        {
            summary.EndTime = DateTime.UtcNow;
        }
    }
}
