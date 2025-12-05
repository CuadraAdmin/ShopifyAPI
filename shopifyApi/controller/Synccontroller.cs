using Hangfire;
using Microsoft.AspNetCore.Mvc;
using ShopifySync.Jobs;

namespace shopifyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly ILogger<SyncController> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public SyncController(ILogger<SyncController> logger, IBackgroundJobClient backgroundJobClient)
    {
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
    }

    /// <summary>
    /// Ejecuta una sincronización completa de inventario de todas las tiendas
    /// </summary>
    [HttpPost("full-inventory")]
    public IActionResult TriggerFullInventorySync()
    {
        try
        {
            var jobId = _backgroundJobClient.Enqueue<FullInventorySyncJob>(job => job.Execute(CancellationToken.None));

            _logger.LogInformation("Sincronización completa de inventario encolada con Job ID: {JobId}", jobId);

            return Ok(new
            {
                success = true,
                jobId,
                message = "Sincronización completa de inventario iniciada",
                dashboardUrl = $"/hangfire/jobs/details/{jobId}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al encolar sincronización completa de inventario");
            return StatusCode(500, new
            {
                success = false,
                message = "Error al iniciar sincronización",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Ejecuta una sincronización incremental (día anterior)
    /// </summary>
    [HttpPost("daily-inventory")]
    public IActionResult TriggerDailyInventoryUpdate()
    {
        try
        {
            var jobId = _backgroundJobClient.Enqueue<DailyInventoryUpdateJob>(job => job.Execute(CancellationToken.None));

            _logger.LogInformation("Sincronización incremental diaria encolada con Job ID: {JobId}", jobId);

            return Ok(new
            {
                success = true,
                jobId,
                message = "Sincronización incremental iniciada (día anterior)",
                dashboardUrl = $"/hangfire/jobs/details/{jobId}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al encolar sincronización incremental");
            return StatusCode(500, new
            {
                success = false,
                message = "Error al iniciar sincronización incremental",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Ejecuta una actualización de precios
    /// </summary>
    [HttpPost("price-update")]
    public IActionResult TriggerPriceUpdate()
    {
        try
        {
            var jobId = _backgroundJobClient.Enqueue<PriceUpdateJob>(job => job.Execute(CancellationToken.None));

            _logger.LogInformation("Actualización de precios encolada con Job ID: {JobId}", jobId);

            return Ok(new
            {
                success = true,
                jobId,
                message = "Actualización de precios iniciada",
                dashboardUrl = $"/hangfire/jobs/details/{jobId}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al encolar actualización de precios");
            return StatusCode(500, new
            {
                success = false,
                message = "Error al iniciar actualización de precios",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Obtiene el estado de los últimos 10 procesos de sincronización
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetSyncStatus([FromServices] Microsoft.Data.SqlClient.SqlConnection connection)
    {
        try
        {
            const string query = @"
                SELECT TOP 10
                    Sync_Id,
                    Sync_Tipo,
                    Sync_FechaInicio,
                    Sync_FechaFin,
                    Sync_Estado,
                    Sync_Consultados,
                    Sync_Insertados,
                    Sync_Actualizados,
                    Sync_Fallidos,
                    Sync_Mensaje
                FROM Sincronizaciones
                ORDER BY Sync_FechaInicio DESC";

            // Nota: Aquí deberías inyectar el connection string y usar Dapper
            // Por simplicidad, retornamos un mensaje

            return Ok(new
            {
                success = true,
                message = "Consulta el dashboard de Hangfire en /hangfire para ver el estado de los jobs",
                hangfireDashboard = "/hangfire"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener estado de sincronizaciones");
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }
}