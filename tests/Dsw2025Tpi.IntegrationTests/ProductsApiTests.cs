using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Tests.Shared;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

// Escenarios de integración del catálogo de productos pedidos por el enunciado del TP4 (CARD-TP4-03).
// La base sembrada por ApiFactory tiene dos productos activos: "Product one" (SKU-1) y "Product two" (SKU-2).
[Trait("Category", "Integration")]
public class ProductsApiTests
{
    [Fact]
    public async Task AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient("Admin");
        var request = ProductRequestFactory.Create(
            sku: "SKU-NEW",
            internalCode: "PRD-NEW",
            name: "New product",
            description: "New description",
            currentUnitPrice: 21.75m,
            stockQuantity: 8);

        using var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var productId = body.GetProperty("id").GetGuid();
        Assert.EndsWith($"/api/products/{productId}", response.Headers.Location!.ToString());
        factory.Inspect(db =>
        {
            Assert.Equal(3, db.Set<Product>().Count());
            var persisted = db.Set<Product>().Single(product => product.Id == productId);
            Assert.Equal(request.Sku, persisted.Sku);
            Assert.Equal(request.InternalCode, persisted.InternalCode);
            Assert.Equal(request.Name, persisted.Name);
            Assert.Equal(request.Description, persisted.Description);
            Assert.Equal(request.CurrentUnitPrice, persisted.CurrentUnitPrice);
            Assert.Equal(request.StockQuantity, persisted.StockQuantity);
            Assert.True(persisted.IsActive);
        });
    }

    [Fact]
    public async Task GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient(role: null);

        using var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Contrato real de la API: ProductModel.ResponsePagination(ProductItems, Total) en camelCase.
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        var items = body.GetProperty("productItems");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(new[] { "Product one", "Product two" }, Names(body));
        var first = items[0];
        Assert.Equal(factory.ProductId, first.GetProperty("id").GetGuid());
        Assert.Equal("SKU-1", first.GetProperty("sku").GetString());
        Assert.Equal("CODE-1", first.GetProperty("internalCode").GetString());
        Assert.Equal(12.50m, first.GetProperty("currentUnitPrice").GetDecimal());
        Assert.Equal(5, first.GetProperty("stockQuantity").GetInt32());
        Assert.True(first.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task GetProducts_PublicEndpoint_WhenCatalogIsEmpty_ReturnsNoContent()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient(role: null);
        factory.Inspect(db =>
        {
            db.Set<Product>().RemoveRange(db.Set<Product>());
            db.SaveChanges();
        });

        using var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("search=mouse&pageNumber=1&pageSize=10", "Mouse Gamer", 1)]
    [InlineData("search=MOUSE&pageNumber=1&pageSize=10", "Mouse Gamer", 1)]
    [InlineData("search=tec-01&pageNumber=1&pageSize=10", "Teclado Mecánico", 1)]
    [InlineData("search=sku&pageNumber=2&pageSize=2", "Product one|Product two", 5)]
    public async Task GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite(
        string query, string expectedNames, int expectedTotal)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient(role: null);
        factory.Inspect(db =>
        {
            db.Set<Product>().AddRange(
                new Product("SKU-MG", "Mouse Gamer", 25m, 10, null, "MOU-01"),
                new Product("SKU-TM", "Teclado Mecánico", 40m, 5, null, "TEC-01"),
                new Product("SKU-MON", "Monitor", 150m, 3, null, "MON-01"));
            db.SaveChanges();
        });

        using var response = await client.GetAsync($"/api/products?{query}");

        // Las búsquedas usan términos ASCII: en SQLite, upper() no convierte letras acentuadas.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedNames.Split('|'), Names(body));
        Assert.Equal(expectedTotal, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient("Admin");
        factory.Inspect(db =>
        {
            db.Set<Product>().Single(product => product.Id == factory.OtherProductId).IsActive = false;
            db.SaveChanges();
        });

        using var adminResponse = await client.GetAsync("/api/products/admin");
        using var publicResponse = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        var adminBody = await adminResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, adminBody.GetProperty("total").GetInt32());
        var inactive = adminBody.GetProperty("productItems").EnumerateArray()
            .Single(product => product.GetProperty("id").GetGuid() == factory.OtherProductId);
        Assert.False(inactive.GetProperty("isActive").GetBoolean());

        // A diferencia del endpoint de Admin, el listado público excluye el producto inactivo.
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var publicBody = await publicResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, publicBody.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "Product one" }, Names(publicBody));
    }

    private static string?[] Names(JsonElement body) =>
        body.GetProperty("productItems").EnumerateArray()
            .Select(product => product.GetProperty("name").GetString())
            .ToArray();
}
