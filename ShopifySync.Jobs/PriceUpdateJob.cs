using Microsoft.Extensions.Logging;
using ShopifySync.Core.Services;

namespace ShopifySync.Jobs;

public class PriceUpdateJob
{
    private readonly InventorySyncService _inventorySyncService;
    private readonly ILogger<PriceUpdateJob> _logger;

    public PriceUpdateJob(InventorySyncService inventorySyncService, ILogger<PriceUpdateJob> logger)
    {
        _inventorySyncService = inventorySyncService;
        _logger = logger;
    }

    public async Task Execute(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[PriceUpdateJob] Iniciando job de actualización de precios Shopify...");
        await _inventorySyncService.RunPriceUpdateAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[PriceUpdateJob] Finalizó job de actualización de precios Shopify.");
    }
}
