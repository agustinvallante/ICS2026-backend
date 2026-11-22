using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

[Trait("Category", "Integration")]
public class OrdersApiTests
{
    private static OrderRequest Request(ApiFactory factory, int quantity = 2) =>
        new(factory.CustomerId, "Shipping street 1", "Billing street 2", "Test order",
            [new OrderItemRequest(factory.ProductId, quantity)]);

    [Fact]
    // Escenario E2E a nivel de API elegido para el TP4: crea la orden por HTTP con un cliente
    // autenticado, verifica la persistencia y el descuento de stock en SQLite y la vuelve a leer por su Location.
    public async Task CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();
        var request = Request(factory);
        request.Items.Add(new OrderItemRequest(factory.OtherProductId, 1));

        using var response = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = body.GetProperty("orderId").GetGuid();
        Assert.Equal(28m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("PENDING", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.EndsWith($"/api/orders/{orderId}", response.Headers.Location!.ToString());
        factory.Inspect(db =>
        {
            var order = db.Set<Order>().Include(o => o.OrderItems).Single();
            Assert.Equal(orderId, order.Id);
            Assert.Equal(factory.CustomerId, order.CustomerId);
            Assert.Equal(request.ShippingAddress, order.ShippingAddress);
            Assert.Equal(request.BillingAddress, order.BillingAddress);
            Assert.Equal(28m, order.TotalAmount);
            Assert.Equal(2, order.OrderItems!.Count);
            Assert.All(order.OrderItems, item => Assert.Equal(order.Id, item.OrderId));
            Assert.Contains(order.OrderItems, i => i.ProductId == factory.ProductId && i.Quantity == 2 && i.UnitPrice == 12.50m);
            Assert.Contains(order.OrderItems, i => i.ProductId == factory.OtherProductId && i.Quantity == 1 && i.UnitPrice == 3m);
            Assert.Equal(3, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity);
            Assert.Equal(1, db.Set<Product>().Single(p => p.Id == factory.OtherProductId).StockQuantity);
        });
        using var fetched = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var saved = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(orderId, saved.GetProperty("orderId").GetGuid());
        Assert.Equal(2, saved.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();
        // "Product one" tiene stock 5 en la base sembrada.
        var request = Request(factory, quantity: 6);

        using var response = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INSUFFICIENT_STOCK", error.GetProperty("Code").GetString());
        var message = error.GetProperty("Message").GetString();
        Assert.Contains("no tiene suficiente stock", message);
        Assert.Contains("Stock actual: 5, solicitado: 6", message);
        AssertUnchanged(factory);
    }

    // Caso límite (revisión PR #19): cantidades repetidas cuya suma desborda int se rechazan
    // como stock insuficiente, sin descontar stock ni guardar la orden
    [Fact]
    public async Task CreateOrder_WhenRepeatedQuantitiesWouldOverflowInt_ReturnsInsufficientStockWithoutChanges()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();
        var request = Request(factory, quantity: 3);
        request.Items.Add(new OrderItemRequest(factory.ProductId, int.MaxValue));

        using var response = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INSUFFICIENT_STOCK", error.GetProperty("Code").GetString());
        AssertUnchanged(factory);
    }

    [Fact]
    public async Task CreateOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient(role: null);

        using var response = await client.PostAsJsonAsync("/api/orders", Request(factory));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertUnchanged(factory);
    }

    [Theory]
    [InlineData("empty-items", 400)]
    [InlineData("zero-quantity", 400)]
    [InlineData("negative-quantity", 400)]
    [InlineData("missing-customer", 404)]
    [InlineData("missing-product", 400)]
    [InlineData("inactive-product", 400)]
    public async Task CreateOrder_WithInvalidData_RejectsWithoutChangingPersistence(string scenario, int expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();
        var request = Request(factory);
        switch (scenario)
        {
            case "empty-items": request.Items.Clear(); break;
            case "zero-quantity": request.Items[0] = new(factory.ProductId, 0); break;
            case "negative-quantity": request.Items[0] = new(factory.ProductId, -1); break;
            case "missing-product": request.Items[0] = new(Guid.NewGuid(), 1); break;
            case "missing-customer":
                var id = Guid.NewGuid();
                request = request with { CustomerId = id };
                Authenticate(client, factory, "User", id);
                break;
            case "inactive-product":
                factory.Inspect(db => { db.Set<Product>().Single(p => p.Id == factory.ProductId).IsActive = false; db.SaveChanges(); });
                break;
        }

        using var response = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("Message").GetString()));
        AssertUnchanged(factory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateOrder_WhenLaterValidationOrSaveFails_RollsBackEarlierStockWrites(bool persistenceFailure)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();
        var request = Request(factory);
        if (persistenceFailure) factory.FailOrderSave = true;
        else request.Items.Add(new OrderItemRequest(factory.OtherProductId, 3));

        using var response = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(persistenceFailure ? HttpStatusCode.InternalServerError : HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        // EV-B02 CA4: error controlado con código estable y mensaje accionable, sin detalles internos
        Assert.Equal(persistenceFailure ? "CHECKOUT_FAILED" : "INSUFFICIENT_STOCK", error.GetProperty("Code").GetString());
        var message = error.GetProperty("Message").GetString();
        if (persistenceFailure)
        {
            Assert.Equal("No se pudo completar la compra. No se registró la orden ni se descontó stock; podés intentarlo nuevamente.", message);
            Assert.DoesNotContain("Simulated", message);
        }
        AssertUnchanged(factory);
    }

    [Fact]
    public async Task Orders_WithDifferentOwners_RestrictReadListAndCreation()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();
        using var created = await client.PostAsJsonAsync("/api/orders", Request(factory));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location;
        Assert.NotNull(location);
        var createdOrder = await created.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = createdOrder.GetProperty("orderId").GetGuid();

        Authenticate(client, factory, "USER", factory.OtherCustomerId);
        var otherRequest = Request(factory) with { CustomerId = factory.OtherCustomerId };
        using var otherCreated = await client.PostAsJsonAsync("/api/orders", otherRequest);
        Assert.Equal(HttpStatusCode.Created, otherCreated.StatusCode);
        var otherOrder = await otherCreated.Content.ReadFromJsonAsync<JsonElement>();
        var otherOrderId = otherOrder.GetProperty("orderId").GetGuid();

        using var forbiddenRead = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
        var error = await forbiddenRead.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("No tienes permiso para acceder a este recurso.", error.GetProperty("Message").GetString());
        Assert.False(error.TryGetProperty("orderId", out _));
        Assert.False(error.TryGetProperty("items", out _));

        var list = await client.GetFromJsonAsync<JsonElement>("/api/orders");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(otherOrderId, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("orderId").GetGuid());
        using var forbiddenCreate = await client.PostAsJsonAsync("/api/orders", Request(factory));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);

        Authenticate(client, factory, "User", factory.CustomerId);
        list = await client.GetFromJsonAsync<JsonElement>($"/api/orders?customerId={factory.OtherCustomerId}");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(orderId, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("orderId").GetGuid());

        Authenticate(client, factory, "Admin", customerId: null);
        using var adminRead = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, adminRead.StatusCode);
        list = await client.GetFromJsonAsync<JsonElement>("/api/orders");
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        var listedOrderIds = list.GetProperty("items").EnumerateArray().Select(order => order.GetProperty("orderId").GetGuid());
        Assert.Equal(new[] { orderId, otherOrderId }.Order(), listedOrderIds.Order());
        using var adminCreate = await client.PostAsJsonAsync("/api/orders", Request(factory));
        Assert.Equal(HttpStatusCode.Forbidden, adminCreate.StatusCode);
        factory.Inspect(db => Assert.Equal(2, db.Set<Order>().Count()));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Support", HttpStatusCode.Forbidden)]
    public async Task ReadOrders_RequiresAnAuthorizedRole(string? role, HttpStatusCode expectedStatus)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient(role);

        using var list = await client.GetAsync("/api/orders");
        using var detail = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(expectedStatus, list.StatusCode);
        Assert.Equal(expectedStatus, detail.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOrders_WithoutValidCustomerIdClaim_ReturnsForbidden(bool useZeroGuid)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();
        Authenticate(client, factory, "User", useZeroGuid ? Guid.Empty : null);

        using var list = await client.GetAsync("/api/orders");
        using var detail = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
    }

    [Theory]
    [InlineData("NOT_A_STATUS")]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("0")]
    public async Task UpdateStatus_AsAdmin_PersistsValidStatusAndRejectsInvalidStatus(string invalidStatus)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();
        using var created = await client.PostAsJsonAsync("/api/orders", Request(factory));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var path = created.Headers.Location + "/status";
        Authenticate(client, factory, "Admin", factory.CustomerId);

        using var updated = await client.PutAsJsonAsync(path, "PROCESSING");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        factory.Inspect(db => Assert.Equal(OrderStatus.PROCESSING, db.Set<Order>().Single().Status));
        using var invalid = await client.PutAsJsonAsync(path, invalidStatus);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        factory.Inspect(db => Assert.Equal(OrderStatus.PROCESSING, db.Set<Order>().Single().Status));
    }

    [Fact]
    public async Task GetOrder_WithMissingId_ReturnsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();

        using var response = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static void AssertUnchanged(ApiFactory factory) => factory.Inspect(db =>
    {
        Assert.Empty(db.Set<Order>());
        Assert.Empty(db.Set<OrderItem>());
        Assert.Equal(5, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity);
        Assert.Equal(2, db.Set<Product>().Single(p => p.Id == factory.OtherProductId).StockQuantity);
    });

    private static void Authenticate(HttpClient client, ApiFactory factory, string role, Guid? customerId)
    {
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<JwtTokenService>()
            .GenerateToken("test-user", role, customerId?.ToString() ?? string.Empty);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
