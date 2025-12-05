using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Extensions.Http;
using ShopifySync.Common.Dtos;
using ShopifySync.Integration.Interfaces;

namespace ShopifySync.Integration.Services;

public class ShopifyInventoryClient : IShopifyInventoryClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ShopifyInventoryClient> _logger;

    public ShopifyInventoryClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ShopifyInventoryClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
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

    public async Task<IReadOnlyList<ShopifyInventoryDto>> GetFullInventoryAsync(string storeName, CancellationToken cancellationToken = default)
    {
        var results = new List<ShopifyInventoryDto>();
        string? cursor = null;
        bool hasNextPage;

        const string query = @"
        query($cursor: String) {
          products(first: 250, after: $cursor) {
            pageInfo {
              hasNextPage
              endCursor
            }
            edges {
              node {
                id
                title
                variants(first: 100) {
                  edges {
                    node {
                      id
                      sku
                      barcode
                      price
                      compareAtPrice
                      inventoryItem {
                        id
                        inventoryLevels(first: 10) {
                          edges {
                            node {
                              location {
                                id
                                name
                              }
                              quantities(names: [""available"", ""incoming"", ""reserved"", ""damaged"", ""on_hand""]) {
                                name
                                quantity
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }";

        do
        {
            var variables = cursor != null ? new { cursor } : null;
            var response = await ExecuteGraphQLQueryAsync(storeName, query, variables, cancellationToken);

            var products = response["data"]?["products"];
            hasNextPage = products?["pageInfo"]?["hasNextPage"]?.Value<bool>() ?? false;
            cursor = products?["pageInfo"]?["endCursor"]?.Value<string>();

            var edges = products?["edges"] as JArray;
            if (edges != null)
            {
                foreach (var edge in edges)
                {
                    var product = edge["node"];
                    var productId = ExtractIdFromGid(product?["id"]?.Value<string>());

                    var variants = product?["variants"]?["edges"] as JArray;
                    if (variants != null)
                    {
                        foreach (var variantEdge in variants)
                        {
                            var variant = variantEdge["node"];
                            var inventoryItem = variant?["inventoryItem"];
                            var inventoryLevels = inventoryItem?["inventoryLevels"]?["edges"] as JArray;

                            if (inventoryLevels != null)
                            {
                                foreach (var levelEdge in inventoryLevels)
                                {
                                    var level = levelEdge["node"];
                                    var dto = MapToInventoryDto(storeName, product, variant, level);
                                    results.Add(dto);
                                }
                            }
                        }
                    }
                }
            }

            _logger.LogInformation("[GetFullInventory] Procesados {Count} productos hasta ahora para {Store}", results.Count, storeName);

            // Pequeña pausa para respetar rate limits
            await Task.Delay(250, cancellationToken);

        } while (hasNextPage && !cancellationToken.IsCancellationRequested);

        _logger.LogInformation("[GetFullInventory] Total de items extraídos: {Count} para {Store}", results.Count, storeName);
        return results;
    }

    public async Task<IReadOnlyList<ShopifyInventoryDto>> GetInventoryIncrementalAsync(string storeName, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var results = new List<ShopifyInventoryDto>();
        string? cursor = null;
        bool hasNextPage;

        var fromDate = fromUtc.ToString("yyyy-MM-dd");
        var queryFilter = $"updated_at:>={fromDate}";

        const string query = @"
        query($cursor: String, $query: String) {
          products(first: 250, after: $cursor, query: $query) {
            pageInfo {
              hasNextPage
              endCursor
            }
            edges {
              node {
                id
                title
                updatedAt
                variants(first: 100) {
                  edges {
                    node {
                      id
                      sku
                      barcode
                      price
                      compareAtPrice
                      inventoryItem {
                        id
                        inventoryLevels(first: 10) {
                          edges {
                            node {
                              location {
                                id
                                name
                              }
                              quantities(names: [""available"", ""incoming"", ""reserved"", ""damaged"", ""on_hand""]) {
                                name
                                quantity
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }";

        do
        {
            var variables = new { cursor, query = queryFilter };
            var response = await ExecuteGraphQLQueryAsync(storeName, query, variables, cancellationToken);

            var products = response["data"]?["products"];
            hasNextPage = products?["pageInfo"]?["hasNextPage"]?.Value<bool>() ?? false;
            cursor = products?["pageInfo"]?["endCursor"]?.Value<string>();

            var edges = products?["edges"] as JArray;
            if (edges != null)
            {
                foreach (var edge in edges)
                {
                    var product = edge["node"];
                    var variants = product?["variants"]?["edges"] as JArray;

                    if (variants != null)
                    {
                        foreach (var variantEdge in variants)
                        {
                            var variant = variantEdge["node"];
                            var inventoryItem = variant?["inventoryItem"];
                            var inventoryLevels = inventoryItem?["inventoryLevels"]?["edges"] as JArray;

                            if (inventoryLevels != null)
                            {
                                foreach (var levelEdge in inventoryLevels)
                                {
                                    var level = levelEdge["node"];
                                    var dto = MapToInventoryDto(storeName, product, variant, level);
                                    results.Add(dto);
                                }
                            }
                        }
                    }
                }
            }

            await Task.Delay(250, cancellationToken);

        } while (hasNextPage && !cancellationToken.IsCancellationRequested);

        _logger.LogInformation("[GetInventoryIncremental] Total de items actualizados: {Count} para {Store}", results.Count, storeName);
        return results;
    }

    public async Task<IReadOnlyList<ShopifyInventoryDto>> GetPricesAsync(string storeName, CancellationToken cancellationToken = default)
    {
        var results = new List<ShopifyInventoryDto>();
        string? cursor = null;
        bool hasNextPage;

        const string query = @"
        query($cursor: String) {
          products(first: 250, after: $cursor) {
            pageInfo {
              hasNextPage
              endCursor
            }
            edges {
              node {
                id
                variants(first: 100) {
                  edges {
                    node {
                      id
                      sku
                      barcode
                      price
                      compareAtPrice
                    }
                  }
                }
              }
            }
          }
        }";

        do
        {
            var variables = cursor != null ? new { cursor } : null;
            var response = await ExecuteGraphQLQueryAsync(storeName, query, variables, cancellationToken);

            var products = response["data"]?["products"];
            hasNextPage = products?["pageInfo"]?["hasNextPage"]?.Value<bool>() ?? false;
            cursor = products?["pageInfo"]?["endCursor"]?.Value<string>();

            var edges = products?["edges"] as JArray;
            if (edges != null)
            {
                foreach (var edge in edges)
                {
                    var product = edge["node"];
                    var productId = ExtractIdFromGid(product?["id"]?.Value<string>());

                    var variants = product?["variants"]?["edges"] as JArray;
                    if (variants != null)
                    {
                        foreach (var variantEdge in variants)
                        {
                            var variant = variantEdge["node"];
                            var dto = new ShopifyInventoryDto
                            {
                                StoreName = storeName,
                                ProductId = productId,
                                VariantId = ExtractIdFromGid(variant?["id"]?.Value<string>()),
                                Sku = variant?["sku"]?.Value<string>(),
                                Ean = variant?["barcode"]?.Value<string>(),
                                Price = variant?["price"]?.Value<decimal?>(),
                                CompareAtPrice = variant?["compareAtPrice"]?.Value<decimal?>()
                            };
                            results.Add(dto);
                        }
                    }
                }
            }

            await Task.Delay(250, cancellationToken);

        } while (hasNextPage && !cancellationToken.IsCancellationRequested);

        _logger.LogInformation("[GetPrices] Total de precios extraídos: {Count} para {Store}", results.Count, storeName);
        return results;
    }

    private async Task<JObject> ExecuteGraphQLQueryAsync(string storeName, string query, object? variables, CancellationToken cancellationToken)
    {
        var client = CreateClient(storeName);
        var policy = CreateRetryPolicy();

        var requestBody = new
        {
            query,
            variables
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        var response = await policy.ExecuteAsync(async () =>
        {
            var result = await client.PostAsync("graphql.json", content, cancellationToken);
            result.EnsureSuccessStatusCode();
            return result;
        });

        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        var jObject = JObject.Parse(jsonResponse);

        // Verificar si hay errores en la respuesta GraphQL
        var errors = jObject["errors"];
        if (errors != null && errors.Any())
        {
            var errorMessages = string.Join(", ", errors.Select(e => e["message"]?.Value<string>()));
            throw new InvalidOperationException($"GraphQL errors: {errorMessages}");
        }

        return jObject;
    }

    private ShopifyInventoryDto MapToInventoryDto(string storeName, JToken? product, JToken? variant, JToken? inventoryLevel)
    {
        var quantities = inventoryLevel?["quantities"] as JArray;
        var location = inventoryLevel?["location"];

        var dto = new ShopifyInventoryDto
        {
            StoreName = storeName,
            ProductId = ExtractIdFromGid(product?["id"]?.Value<string>()),
            VariantId = ExtractIdFromGid(variant?["id"]?.Value<string>()),
            InventoryItemId = ExtractIdFromGid(variant?["inventoryItem"]?["id"]?.Value<string>()),
            Sku = variant?["sku"]?.Value<string>(),
            Ean = variant?["barcode"]?.Value<string>(),
            LocationName = location?["name"]?.Value<string>(),
            Price = variant?["price"]?.Value<decimal?>(),
            CompareAtPrice = variant?["compareAtPrice"]?.Value<decimal?>()
        };

        if (quantities != null)
        {
            foreach (var qty in quantities)
            {
                var name = qty["name"]?.Value<string>();
                var quantity = qty["quantity"]?.Value<int>() ?? 0;

                switch (name?.ToLower())
                {
                    case "available":
                        dto.Available = quantity;
                        break;
                    case "incoming":
                        dto.Incoming = quantity;
                        break;
                    case "reserved":
                        dto.Reserved = quantity;
                        break;
                    case "damaged":
                        dto.Damaged = quantity;
                        break;
                    case "on_hand":
                        dto.OnHand = quantity;
                        break;
                }
            }
        }

        return dto;
    }

    private static string? ExtractIdFromGid(string? gid)
    {
        if (string.IsNullOrEmpty(gid)) return null;

        // GID format: gid://shopify/Product/12345
        var parts = gid.Split('/');
        return parts.Length > 0 ? parts[^1] : gid;
    }
}