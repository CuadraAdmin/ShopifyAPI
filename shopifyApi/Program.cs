using Hangfire;
using Hangfire.SqlServer;
using Microsoft.OpenApi.Models;
using ShopifySync.Core.Services;
using ShopifySync.DataAccess.Interfaces;
using ShopifySync.DataAccess.Repositories;
using ShopifySync.Integration.Interfaces;
using ShopifySync.Integration.Services;
using ShopifySync.Jobs;

var builder = WebApplication.CreateBuilder(args);

// ======================================================
// CONFIGURACIÓN DE SERVICIOS
// ======================================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// SWAGGER CONFIG
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Shopify Inventory Sync API",
        Version = "v1",
        Description = "API para sincronización automática de inventarios de Shopify con ERP",
        Contact = new OpenApiContact
        {
            Name = "Sergio Vázquez",
            Email = "sergio.vazquez@cuadra.com.mx"
        }
    });
});

// HANGFIRE CONFIG
builder.Services.AddHangfire(config =>
{
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
          .UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"), new SqlServerStorageOptions
          {
              SchemaName = "HangFireShopify",
              CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
              SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
              QueuePollInterval = TimeSpan.Zero,
              UseRecommendedIsolationLevel = true,
              DisableGlobalLocks = true
          });
});

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = builder.Configuration.GetValue<int>("Hangfire:WorkerCount", 20);
    options.ServerName = $"ShopifyInventorySync-{Environment.MachineName}";
});

// REPOSITORIOS
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection en appsettings");

builder.Services.AddScoped<IInventoryRepository>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<InventoryRepository>>();
    return new InventoryRepository(connectionString, logger);
});

builder.Services.AddScoped<ISyncLogRepository>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<SyncLogRepository>>();
    return new SyncLogRepository(connectionString, logger);
});

// INTEGRACIÓN SHOPIFY
builder.Services.AddHttpClient("shopify", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddScoped<IShopifyInventoryClient, ShopifyInventoryClient>();

// SERVICIO DE NEGOCIO
builder.Services.AddScoped<InventorySyncService>();

// JOBS
builder.Services.AddScoped<FullInventorySyncJob>();
builder.Services.AddScoped<DailyInventoryUpdateJob>();
builder.Services.AddScoped<PriceUpdateJob>();

// LOGGING
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// HEALTH CHECKS
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "database",
        timeout: TimeSpan.FromSeconds(5),
        tags: new[] { "db", "sql" })
    .AddHangfire(options =>
    {
        options.MinimumAvailableServers = 1;
    }, name: "hangfire", tags: new[] { "hangfire" });

var app = builder.Build();

// ======================================================
// PIPELINE
// ======================================================

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Shopify Inventory Sync API v1");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "Shopify Inventory Sync";
});

app.UseRouting();
app.UseCors("AllowAll");

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description ?? "OK",
                duration = $"{e.Value.Duration.TotalMilliseconds:F0}ms"
            }),
            totalDuration = $"{report.TotalDuration.TotalMilliseconds:F0}ms"
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await context.Response.WriteAsync(result);
    }
});

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Shopify Inventory Sync Jobs",
    StatsPollingInterval = 5000,
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// ======================================================
// REGISTRO DE JOBS
// ======================================================

RecurringJob.AddOrUpdate<FullInventorySyncJob>(
    "shopify-full-inventory-sync",
    job => job.Execute(CancellationToken.None),
    builder.Configuration["Sync:FullInventoryCron"] ?? "0 2 * * 0", // Domingo 2:00 AM
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }
);

RecurringJob.AddOrUpdate<DailyInventoryUpdateJob>(
    "shopify-daily-inventory-update",
    job => job.Execute(CancellationToken.None),
    builder.Configuration["Sync:DailyInventoryCron"] ?? "0 12 * * *", // Todos los días 12:00 PM
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }
);

RecurringJob.AddOrUpdate<PriceUpdateJob>(
    "shopify-price-update",
    job => job.Execute(CancellationToken.None),
    builder.Configuration["Sync:PriceUpdateCron"] ?? "0 6 * * *", // Todos los días 6:00 AM
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }
);

app.Run();