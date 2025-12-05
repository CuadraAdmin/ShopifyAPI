using ShopifySync.Common.Dtos;

namespace ShopifySync.Integration.Interfaces;

public interface IShopifyInventoryClient
{
    Task<IReadOnlyList<ShopifyInventoryDto>> GetFullInventoryAsync(string storeName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopifyInventoryDto>> GetInventoryIncrementalAsync(string storeName, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopifyInventoryDto>> GetPricesAsync(string storeName, CancellationToken cancellationToken = default);
}
