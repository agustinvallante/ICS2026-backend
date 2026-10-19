using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Data.Repositories;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.Tests.Integration;

// AT-B04 on the real provider: SQL Server rowversion, separate DbContexts per request/scope and the
// EfUnitOfWork retry. Conflicts are made deterministic through ApiFactory.BeforeProductUpdate.
// CA4 (multi-product atomicity) is covered by SqlServerPersistenceTests.CreateOrder_WhenLaterValidationOrSaveFails_RollsBackOnSqlServer.
[Trait("Category", "Integration")]
[Trait("Provider", "SqlServer")]
public class OrderConcurrencySqlServerTests
{
    private static readonly TimeSpan HoldTimeout = TimeSpan.FromSeconds(15);

    private static OrderRequest Request(ApiFactory factory, int quantity) =>
        new(factory.CustomerId, "Shipping", "Billing", null, [new OrderItemRequest(factory.ProductId, quantity)]);

    // Writes from a separate scope/DbContext without SaveChanges; SQL Server regenerates the rowversion.
    private static async Task SetStockFromAnotherContext(ApiFactory factory, int stock)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        await db.Set<Product>().Where(p => p.Id == factory.ProductId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, stock));
    }

    // Holds the first product update of each request until all of them have read the same stock and RowVersion.
    private static void HoldFirstUpdatesUntilAllRead(ApiFactory factory, int requests)
    {
        var allRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        factory.BeforeProductUpdate = async attempt =>
        {
            if (attempt > requests) return;
            if (Interlocked.Increment(ref arrived) == requests) allRead.SetResult();
            await allRead.Task.WaitAsync(HoldTimeout);
        };
    }

    private static async Task<HttpResponseMessage[]> PostConcurrently(HttpClient client, IEnumerable<OrderRequest> requests) =>
        await Task.WhenAll(requests.Select(r => client.PostAsJsonAsync("/api/orders", r)));

    [Fact]
    public async Task CreateOrder_TwoConcurrentPurchasesWithStockOne_OnlyOneSucceeds()
    {
        using var factory = ApiFactory.ForSqlServer();
        using var client = factory.CreateSeededClient();
        await SetStockFromAnotherContext(factory, 1);
        HoldFirstUpdatesUntilAllRead(factory, 2);

        var responses = await PostConcurrently(client, [Request(factory, 1), Request(factory, 1)]);

        try
        {
            var created = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            var rejected = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created);
            // The loser hits the RowVersion conflict, retries, rereads stock 0 and is rejected as insufficient stock
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("Message").GetString()));
            Assert.Equal(1, factory.ConcurrencyConflictsDetected);
            var orderId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();
            factory.Inspect(db =>
            {
                Assert.Equal(0, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity);
                var order = db.Set<Order>().Include(o => o.OrderItems).Single();
                Assert.Equal(orderId, order.Id);
                var item = Assert.Single(order.OrderItems!);
                Assert.Equal(factory.ProductId, item.ProductId);
                Assert.Equal(1, item.Quantity);
                Assert.Single(db.Set<OrderItem>());
            });
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task CreateOrder_ManyConcurrentPurchasesOverLimitedStock_NeverOversellsAndMatchesOrders()
    {
        const int initialStock = 5;
        int[] quantities = [2, 2, 2, 1, 1, 1];
        using var factory = ApiFactory.ForSqlServer();
        using var client = factory.CreateSeededClient();
        await SetStockFromAnotherContext(factory, initialStock);
        HoldFirstUpdatesUntilAllRead(factory, quantities.Length);

        var responses = await PostConcurrently(client, quantities.Select(q => Request(factory, q)));

        try
        {
            var outcomes = quantities.Zip(responses, (quantity, response) => (quantity, response.StatusCode)).ToArray();
            // Rejections are controlled: insufficient stock after rereading (400) or retries exhausted (409)
            Assert.All(outcomes, o => Assert.Contains(o.StatusCode,
                new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
            var succeeded = outcomes.Where(o => o.StatusCode == HttpStatusCode.Created).ToArray();
            Assert.NotEmpty(succeeded);
            Assert.True(factory.ConcurrencyConflictsDetected >= 1);
            var createdIds = new List<Guid>();
            foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Created))
                createdIds.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid());

            factory.Inspect(db =>
            {
                var finalStock = db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity;
                var orders = db.Set<Order>().Include(o => o.OrderItems).ToList();
                Assert.True(finalStock >= 0);
                Assert.Equal(createdIds.Order(), orders.Select(o => o.Id).Order());
                Assert.Equal(succeeded.Sum(o => o.quantity), initialStock - finalStock);
                Assert.Equal(initialStock - finalStock, db.Set<OrderItem>().Sum(i => i.Quantity));
                Assert.All(orders, o => Assert.Single(o.OrderItems!));
                // Stock only decreases, so an insufficient-stock rejection implies it is still insufficient at the end
                Assert.All(outcomes.Where(o => o.StatusCode == HttpStatusCode.BadRequest),
                    o => Assert.True(finalStock < o.quantity));
            });
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task CreateOrder_ProductChangedAfterReadAndStockNoLongerEnough_DetectsConflictAndRejects()
    {
        using var factory = ApiFactory.ForSqlServer();
        using var client = factory.CreateSeededClient();
        factory.BeforeProductUpdate = async attempt =>
        {
            if (attempt == 1) await SetStockFromAnotherContext(factory, 1);
        };

        using var response = await client.PostAsJsonAsync("/api/orders", Request(factory, 2));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, factory.ConcurrencyConflictsDetected);
        // The retry reread stock 1 and rejected the purchase before attempting another update
        Assert.Equal(1, factory.ProductUpdateAttempts);
        factory.Inspect(db =>
        {
            Assert.Equal(1, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity);
            Assert.Empty(db.Set<Order>());
            Assert.Empty(db.Set<OrderItem>());
        });
    }

    [Fact]
    public async Task CreateOrder_ProductChangedAfterReadButStockStillEnough_RetriesWithFreshStock()
    {
        using var factory = ApiFactory.ForSqlServer();
        using var client = factory.CreateSeededClient();
        factory.BeforeProductUpdate = async attempt =>
        {
            if (attempt == 1) await SetStockFromAnotherContext(factory, 4);
        };

        using var response = await client.PostAsJsonAsync("/api/orders", Request(factory, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, factory.ConcurrencyConflictsDetected);
        Assert.Equal(2, factory.ProductUpdateAttempts);
        factory.Inspect(db =>
        {
            // 4 - 2 from the reread value; the stale read would have written 5 - 2 = 3 (lost update)
            Assert.Equal(2, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity);
            var order = db.Set<Order>().Include(o => o.OrderItems).Single();
            Assert.Equal(2, Assert.Single(order.OrderItems!).Quantity);
        });
    }

    [Fact]
    public async Task CreateOrder_ProductChangedOnEveryAttempt_ReturnsConflictAfterMaxAttempts()
    {
        using var factory = ApiFactory.ForSqlServer();
        using var client = factory.CreateSeededClient();
        factory.BeforeProductUpdate = attempt => SetStockFromAnotherContext(factory, 5 - attempt);

        using var response = await client.PostAsJsonAsync("/api/orders", Request(factory, 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Los datos fueron modificados por otra operación. Intentá nuevamente.",
            error.GetProperty("Message").GetString());
        Assert.Equal(EfUnitOfWork.MaxAttempts, factory.ProductUpdateAttempts);
        Assert.Equal(EfUnitOfWork.MaxAttempts, factory.ConcurrencyConflictsDetected);
        factory.Inspect(db =>
        {
            Assert.Equal(5 - EfUnitOfWork.MaxAttempts, db.Set<Product>().Single(p => p.Id == factory.ProductId).StockQuantity);
            Assert.Empty(db.Set<Order>());
            Assert.Empty(db.Set<OrderItem>());
        });
    }
}
