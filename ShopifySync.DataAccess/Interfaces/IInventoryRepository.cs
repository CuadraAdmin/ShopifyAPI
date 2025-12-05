using ShopifySync.Common.Dtos;

namespace ShopifySync.DataAccess.Interfaces;

public interface IInventoryRepository
{
    Task<(bool Exists, bool IsUpdated)> UpsertInventoryAsync(ShopifyInventoryDto item, string platform, CancellationToken cancellationToken = default);
}
