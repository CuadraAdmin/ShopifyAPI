using Microsoft.Extensions.Logging;
using ShopifySync.Core.Services;

namespace ShopifySync.Jobs;

public class DailyInventoryUpdateJob
{
    private readonly InventorySyncService _inventorySyncService;
    private readonly ILogger<DailyInventoryUpdateJob> _logger;

    public DailyInventoryUpdateJob(InventorySyncService inventorySyncService, ILogger<DailyInventoryUpdateJob> logger)
    {
        _inventorySyncService = inventorySyncService;
        _logger = logger;
    }

    public async Task Execute(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DailyInventoryUpdateJob] Iniciando job de sincronización incremental diaria de inventario Shopify...");
        await _inventorySyncService.RunIncrementalInventorySyncAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[DailyInventoryUpdateJob] Finalizó job de sincronización incremental diaria de inventario Shopify.");
    }
}
