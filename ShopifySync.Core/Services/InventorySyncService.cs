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
        var stores = _configuration.GetSection("Shopify:Stores").Get<string[]>();
        return stores?.Length > 0 ? stores : new[] { "Default" };
    }

    public async Task RunFullInventorySyncAsync(CancellationToken cancellationToken = default)
    {
        // Crear registro de sincronización
        var syncId = await _syncLogRepository.CreateSyncRecordAsync("FullInventory", cancellationToken);
        _syncLogRepository.SetCurrentSyncId(syncId);

        var summary = new SyncSummary
        {
            SyncType = "FullInventory",
            StartTime = DateTime.UtcNow
        };

        _logger.LogInformation("[FullInventory] Iniciando sincronización completa. Sync_Id: {SyncId}", syncId);

        try
        {
            foreach (var store in GetStores())
            {
                _logger.LogInformation("[FullInventory] Procesando tienda: {Store}", store);

                var items = await _shopifyClient.GetFullInventoryAsync(store, cancellationToken).ConfigureAwait(false);
                summary.TotalItems += items.Count;

                _logger.LogInformation("[FullInventory] Obtenidos {Count} items de {Store}", items.Count, store);

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

                        // Log de éxito solo para los primeros 100 items (evitar saturar la BD)
                        if (summary.TotalItems <= 100)
                        {
                            var successLog = new SyncLogEntry
                            {
                                Type = "Success",
                                Identifier = $"SKU:{item.Sku}",
                                Message = result.Exists ? "Actualizado" : "Insertado",
                                DetailJson = $"{{\"ProductId\":\"{item.ProductId}\",\"Location\":\"{item.LocationName}\"}}"
                            };
                            await _syncLogRepository.SaveLogAsync(successLog, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        summary.Failed++;
                        _logger.LogError(ex, "[FullInventory] Error al sincronizar SKU: {Sku}", item.Sku);

                        var errorLog = new SyncLogEntry
                        {
                            Type = "Error",
                            Identifier = $"SKU:{item.Sku ?? "NULL"}",
                            Message = "Error al procesar item",
                            DetailJson = ex.Message
                        };
                        await _syncLogRepository.SaveLogAsync(errorLog, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            summary.Status = summary.Failed > 0 ? "CompletadoConErrores" : "Completado";
            summary.Message = $"Consultados: {summary.TotalItems}, Insertados: {summary.Inserted}, Actualizados: {summary.Updated}, Fallidos: {summary.Failed}";
        }
        catch (Exception ex)
        {
            summary.Status = "Fallido";
            summary.Message = ex.Message;
            _logger.LogError(ex, "[FullInventory] Error crítico en sincronización");

            var errorLog = new SyncLogEntry
            {
                Type = "Error",
                Identifier = "SYNC",
                Message = "Error crítico en sincronización",
                DetailJson = ex.ToString()
            };
            await _syncLogRepository.SaveLogAsync(errorLog, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            summary.EndTime = DateTime.UtcNow;

            // Actualizar registro de sincronización
            await _syncLogRepository.UpdateSyncRecordAsync(
                syncId,
                summary.Status,
                summary.TotalItems,
                summary.Inserted,
                summary.Updated,
                summary.Failed,
                summary.Message,
                cancellationToken
            );

            _logger.LogInformation(
                "[FullInventory] Finalizado. Status: {Status}, Total: {Total}, Insertados: {Inserted}, Actualizados: {Updated}, Fallidos: {Failed}",
                summary.Status, summary.TotalItems, summary.Inserted, summary.Updated, summary.Failed
            );
        }
    }

    public async Task RunIncrementalInventorySyncAsync(CancellationToken cancellationToken = default)
    {
        var syncId = await _syncLogRepository.CreateSyncRecordAsync("IncrementalInventory", cancellationToken);
        _syncLogRepository.SetCurrentSyncId(syncId);

        var fechaInicio = DateTime.UtcNow.Date.AddDays(-1);
        var fechaFin = fechaInicio.AddDays(1).AddTicks(-1);

        var summary = new SyncSummary
        {
            SyncType = "IncrementalInventory",
            StartTime = DateTime.UtcNow
        };

        _logger.LogInformation("[IncrementalInventory] Sincronización incremental de {From} a {To}. Sync_Id: {SyncId}",
            fechaInicio, fechaFin, syncId);

        try
        {
            foreach (var store in GetStores())
            {
                _logger.LogInformation("[IncrementalInventory] Procesando tienda: {Store}", store);

                var items = await _shopifyClient
                    .GetInventoryIncrementalAsync(store, fechaInicio, fechaFin, cancellationToken)
                    .ConfigureAwait(false);

                summary.TotalItems += items.Count;

                _logger.LogInformation("[IncrementalInventory] Obtenidos {Count} items actualizados de {Store}", items.Count, store);

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
                        _logger.LogError(ex, "[IncrementalInventory] Error al sincronizar SKU: {Sku}", item.Sku);

                        var errorLog = new SyncLogEntry
                        {
                            Type = "Error",
                            Identifier = $"SKU:{item.Sku ?? "NULL"}",
                            Message = "Error al procesar item incremental",
                            DetailJson = ex.Message
                        };
                        await _syncLogRepository.SaveLogAsync(errorLog, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            summary.Status = summary.Failed > 0 ? "CompletadoConErrores" : "Completado";
            summary.Message = $"Consultados: {summary.TotalItems}, Insertados: {summary.Inserted}, Actualizados: {summary.Updated}, Fallidos: {summary.Failed}";
        }
        catch (Exception ex)
        {
            summary.Status = "Fallido";
            summary.Message = ex.Message;
            _logger.LogError(ex, "[IncrementalInventory] Error crítico");
        }
        finally
        {
            summary.EndTime = DateTime.UtcNow;

            await _syncLogRepository.UpdateSyncRecordAsync(
                syncId,
                summary.Status,
                summary.TotalItems,
                summary.Inserted,
                summary.Updated,
                summary.Failed,
                summary.Message,
                cancellationToken
            );

            _logger.LogInformation(
                "[IncrementalInventory] Finalizado. Status: {Status}, Total: {Total}, Insertados: {Inserted}, Actualizados: {Updated}, Fallidos: {Failed}",
                summary.Status, summary.TotalItems, summary.Inserted, summary.Updated, summary.Failed
            );
        }
    }

    public async Task RunPriceUpdateAsync(CancellationToken cancellationToken = default)
    {
        var syncId = await _syncLogRepository.CreateSyncRecordAsync("PriceUpdate", cancellationToken);
        _syncLogRepository.SetCurrentSyncId(syncId);

        var summary = new SyncSummary
        {
            SyncType = "PriceUpdate",
            StartTime = DateTime.UtcNow
        };

        _logger.LogInformation("[PriceUpdate] Iniciando actualización de precios. Sync_Id: {SyncId}", syncId);

        try
        {
            foreach (var store in GetStores())
            {
                _logger.LogInformation("[PriceUpdate] Procesando tienda: {Store}", store);

                var items = await _shopifyClient.GetPricesAsync(store, cancellationToken).ConfigureAwait(false);
                summary.TotalItems += items.Count;

                _logger.LogInformation("[PriceUpdate] Obtenidos {Count} precios de {Store}", items.Count, store);

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
                        _logger.LogError(ex, "[PriceUpdate] Error al actualizar precio para SKU: {Sku}", item.Sku);
                    }
                }
            }

            summary.Status = summary.Failed > 0 ? "CompletadoConErrores" : "Completado";
            summary.Message = $"Consultados: {summary.TotalItems}, Insertados: {summary.Inserted}, Actualizados: {summary.Updated}, Fallidos: {summary.Failed}";
        }
        catch (Exception ex)
        {
            summary.Status = "Fallido";
            summary.Message = ex.Message;
            _logger.LogError(ex, "[PriceUpdate] Error crítico");
        }
        finally
        {
            summary.EndTime = DateTime.UtcNow;

            await _syncLogRepository.UpdateSyncRecordAsync(
                syncId,
                summary.Status,
                summary.TotalItems,
                summary.Inserted,
                summary.Updated,
                summary.Failed,
                summary.Message,
                cancellationToken
            );

            _logger.LogInformation(
                "[PriceUpdate] Finalizado. Status: {Status}, Total: {Total}, Actualizados: {Updated}, Fallidos: {Failed}",
                summary.Status, summary.TotalItems, summary.Updated, summary.Failed
            );
        }
    }
}