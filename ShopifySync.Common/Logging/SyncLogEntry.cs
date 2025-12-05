namespace ShopifySync.Common.Logging;

public class SyncLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty; // Success, Error, Warning
    public string Identifier { get; set; } = string.Empty; // SKU-123, ProductId-456, EAN-...
    public string Message { get; set; } = string.Empty;
    public string? DetailJson { get; set; }
}
