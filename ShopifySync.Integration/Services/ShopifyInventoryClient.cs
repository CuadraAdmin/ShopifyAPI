using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Polly;
using Polly.Extensions.Http;
using ShopifySync.Common.Dtos;
using ShopifySync.Integration.Interfaces;

namespace ShopifySync.Integration.Services;

public class ShopifyInventoryClient : IShopifyInventoryClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public ShopifyInventoryClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    private HttpClient CreateClient(string storeName)
    {
        var client = _httpClientFactory.CreateClient("shopify");

        var baseUrl = _configuration[$"Shopify:{storeName}:BaseUrl"]
                     ?? _configuration["Shopify:BaseUrl"]
                     ?? throw new InvalidOperationException($"BaseUrl de Shopify no configurado para store '{storeName}'");

        var accessToken = _configuration[$"Shopify:{storeName}:AccessToken"]
                         ?? _configuration["Shopify:AccessToken"]
                         ?? throw new InvalidOperationException($"AccessToken de Shopify no configurado para store '{storeName}'");

        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("X-Shopify-Access-Token", accessToken);

        return client;
    }

    private static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy()
        => HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => (int)r.StatusCode == 429)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

    public Task<IReadOnlyList<ShopifyInventoryDto>> GetFullInventoryAsync(string storeName, CancellationToken cancellationToken = default)
    {
        // Aquí debes implementar la llamada GraphQL a Shopify con paginación cursor-based.
        // Dejamos un esqueleto para que el resto del flujo compile.
        return Task.FromResult<IReadOnlyList<ShopifyInventoryDto>>(Array.Empty<ShopifyInventoryDto>());
    }

    public Task<IReadOnlyList<ShopifyInventoryDto>> GetInventoryIncrementalAsync(string storeName, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        // Aquí debes implementar el query filtrado por updated_at >= fromUtc.
        return Task.FromResult<IReadOnlyList<ShopifyInventoryDto>>(Array.Empty<ShopifyInventoryDto>());
    }

    public Task<IReadOnlyList<ShopifyInventoryDto>> GetPricesAsync(string storeName, CancellationToken cancellationToken = default)
    {
        // Aquí debes implementar la obtención de precios (product_id, variant_id, price, compare_at_price).
        return Task.FromResult<IReadOnlyList<ShopifyInventoryDto>>(Array.Empty<ShopifyInventoryDto>());
    }
}
