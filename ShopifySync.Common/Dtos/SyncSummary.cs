namespace ShopifySync.Common.Dtos;

public class SyncSummary
{
    public Guid SyncId { get; set; } = Guid.NewGuid();
    public string SyncType { get; set; } = string.Empty; // FullInventory, IncrementalInventory, PriceUpdate
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = "EnProceso"; // EnProceso, Completado, CompletadoConErrores, Fallido
    public string? Message { get; set; }

    public int TotalItems { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
}
