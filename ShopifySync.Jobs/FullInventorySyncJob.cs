using Microsoft.Extensions.Logging;
using ShopifySync.Core.Services;

namespace ShopifySync.Jobs;

public class FullInventorySyncJob
{
    private readonly InventorySyncService _inventorySyncService;
    private readonly ILogger<FullInventorySyncJob> _logger;

    public FullInventorySyncJob(InventorySyncService inventorySyncService, ILogger<FullInventorySyncJob> logger)
    {
        _inventorySyncService = inventorySyncService;
        _logger = logger;
    }

    public async Task Execute(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[FullInventorySyncJob] Iniciando job de sincronización full de inventario Shopify...");
        await _inventorySyncService.RunFullInventorySyncAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[FullInventorySyncJob] Finalizó job de sincronización full de inventario Shopify.");
    }
}
