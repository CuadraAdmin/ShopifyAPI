namespace ShopifySync.Common.Dtos;

public class ShopifyInventoryDto
{
    public string? StoreName { get; set; }
    public string? Sku { get; set; }
    public string? Ean { get; set; }
    public string? ProductId { get; set; }
    public string? VariantId { get; set; }
    public string? InventoryItemId { get; set; }
    public string? LocationName { get; set; }

    public int Available { get; set; }
    public int Incoming { get; set; }
    public int Reserved { get; set; }
    public int Damaged { get; set; }
    public int OnHand { get; set; }

    public decimal? Price { get; set; }
    public decimal? CompareAtPrice { get; set; }

    /// <summary>
    /// Id interno de producto (Prod_Id) cuando se encuentra match por EAN en la BD.
    /// </summary>
    public int? ProdId { get; set; }
}
