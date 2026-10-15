using System.Linq.Expressions;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Data.Repositories;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using Dsw2025Tpi.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Dsw2025Tpi.Api.NewFolder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;

var database = "ATB04_QA_" + Guid.NewGuid().ToString("N");
var connectionString = $"Server=.\\SQLEXPRESS;Database={database};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=10";
var options = new DbContextOptionsBuilder<Dsw2025TpiContext>()
    .UseSqlServer(connectionString).Options;
Dsw2025TpiContext Context() => new(options);
void Check(bool value, string message) { if (!value) throw new Exception(message); }
var failures = 0;
async Task Case(string name, Func<Task> test)
{
    try { await test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex}"); }
}
async Task<(Guid customer, Guid[] products)> Seed(params int[] stocks)
{
    await using var db = Context();
    var customer = new Customer { Name = "QA", Email = Guid.NewGuid() + "@example.test" };
    db.Add(customer);
    var products = stocks.Select(stock => new Product(Guid.NewGuid().ToString(), "QA", 10m, stock, null, Guid.NewGuid().ToString())).ToArray();
    db.AddRange(products);
    await db.SaveChangesAsync();
    return (customer.Id, products.Select(p => p.Id).ToArray());
}
OrderRequest Request(Guid customer, params (Guid id, int quantity)[] items) =>
    new(customer, "QA shipping", "QA billing", null, items.Select(i => new OrderItemRequest(i.id, i.quantity)).ToList());
async Task<string> Buy(OrderRequest request, Func<Product, Task>? afterRead = null)
{
    await using var db = Context();
    IRepository repository = new ReadHookRepository(new EfRepository(db), afterRead);
    var service = new OrdersManagementService(repository, new EfUnitOfWork(db, NullLogger<EfUnitOfWork>.Instance), NullLogger<OrdersManagementService>.Instance);
    try { await service.CreateOrder(request); return "created"; }
    catch (InsufficientStockException) { return "stock"; }
    catch (ConcurrencyConflictException) { return "conflict"; }
}
Console.WriteLine($"Commit under QA: 913c2ea; isolated SQL Server database: {database}");
var created = false;
try
{
    await using (var db = Context())
    {
        await db.Database.MigrateAsync();
        created = true;
        var pending = await db.Database.GetPendingMigrationsAsync();
        Check(!pending.Any(), "Pending migrations");
        Console.WriteLine("PASS all migrations applied on empty SQL Server database");
    }
    await Case("CA1: two buyers read the same last unit (20 rounds)", async () =>
    {
        for (var round = 0; round < 20; round++)
        {
            var (customer, products) = await Seed(1);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            async Task Hook(Product _) { if (Interlocked.Increment(ref reads) <= 2) { if (reads == 2) ready.TrySetResult(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(20)); } }
            var request = Request(customer, (products[0], 1));
            var results = await Task.WhenAll(Buy(request, Hook), Buy(request, Hook));
            await using var db = Context();
            Check(results.Count(r => r == "created") == 1 && results.Count(r => r == "stock") == 1, string.Join(",", results));
            Check((await db.Set<Product>().FindAsync(products[0]))!.StockQuantity == 0, "Final stock must be zero");
            Check(await db.Set<Order>().CountAsync(o => o.CustomerId == customer) == 1, "Exactly one order must persist");
        }
    });
    await Case("CA2: 40 concurrent requests, stock=10 (5 rounds)", async () =>
    {
        for (var round = 0; round < 5; round++)
        {
            var (customer, products) = await Seed(10);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 40).Select(async _ => { await start.Task; return await Buy(Request(customer, (products[0], 1))); }).ToArray();
            start.SetResult();
            var results = await Task.WhenAll(tasks);
            await using var db = Context();
            var stock = (await db.Set<Product>().FindAsync(products[0]))!.StockQuantity;
            var orders = await db.Set<Order>().CountAsync(o => o.CustomerId == customer);
            Check(stock >= 0 && stock + orders == 10, $"Stock={stock}, orders={orders}");
            Check(orders == results.Count(r => r == "created"), "Persisted order count mismatch");
            Console.WriteLine($"  round {round + 1}: created={orders}, stock={stock}, insufficient={results.Count(r => r == "stock")}, conflicts={results.Count(r => r == "conflict")}");
        }
    });
    foreach (var remaining in new[] { 0, 2 })
    await Case($"CA3: external update after read, new stock={remaining}", async () =>
    {
        var (customer, products) = await Seed(1);
        var reads = 0;
        async Task Hook(Product product)
        {
            if (Interlocked.Increment(ref reads) != 1) return;
            await using var external = Context();
            await external.Set<Product>().Where(p => p.Id == product.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, remaining));
        }
        var result = await Buy(Request(customer, (products[0], 1)), Hook);
        await using var db = Context();
        Check(reads >= 2, "Product was not re-read after the conflict");
        Check(result == (remaining == 0 ? "stock" : "created"), $"Unexpected result: {result}");
        Check((await db.Set<Product>().FindAsync(products[0]))!.StockQuantity == Math.Max(0, remaining - 1), "Incorrect stock after revalidation");
        Check(await db.Set<Order>().CountAsync(o => o.CustomerId == customer) == (remaining == 0 ? 0 : 1), "Incorrect persisted orders");
    });
    await Case("CA4: second item has insufficient stock; rollback first item", async () =>
    {
        var (customer, products) = await Seed(5, 0);
        Check(await Buy(Request(customer, (products[0], 2), (products[1], 1))) == "stock", "Expected rejection");
        await using var db = Context();
        Check((await db.Set<Product>().FindAsync(products[0]))!.StockQuantity == 5, "First item was not rolled back");
        Check((await db.Set<Product>().FindAsync(products[1]))!.StockQuantity == 0, "Second stock changed");
        Check(!await db.Set<Order>().AnyAsync(o => o.CustomerId == customer), "Rejected order persisted");
    });
    await Case("Duplicate product lines cannot oversell", async () =>
    {
        var (customer, products) = await Seed(1);
        Check(await Buy(Request(customer, (products[0], 1), (products[0], 1))) == "stock", "Expected rejection");
        await using var db = Context();
        Check((await db.Set<Product>().FindAsync(products[0]))!.StockQuantity == 1, "Duplicate lines did not roll back");
        Check(!await db.Set<Order>().AnyAsync(o => o.CustomerId == customer), "Rejected order persisted");
    });
    await Case("Retry exhaustion rolls back stock and returns domain conflict", async () =>
    {
        var (customer, products) = await Seed(4);
        var reads = 0;
        async Task Hook(Product product)
        {
            Interlocked.Increment(ref reads);
            await using var external = Context();
            await external.Set<Product>().Where(p => p.Id == product.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Description, Guid.NewGuid().ToString()));
        }
        Check(await Buy(Request(customer, (products[0], 1)), Hook) == "conflict", "Expected conflict");
        Check(reads == EfUnitOfWork.MaxAttempts, "Incorrect attempt count");
        await using var db = Context();
        Check((await db.Set<Product>().FindAsync(products[0]))!.StockQuantity == 4, "Stock changed after failed retries");
        Check(!await db.Set<Order>().AnyAsync(o => o.CustomerId == customer), "Failed order persisted");
    });
    await Case("Middleware maps insufficient stock to 400 and conflicts to 409", async () =>
    {
        foreach (var (error, expected) in new (Exception, int)[] {
            (new InsufficientStockException("QA", 0, 1), 400),
            (new DbUpdateConcurrencyException("QA internal details"), 409),
            (new ConcurrencyConflictException(new Exception("QA internal details")), 409) })
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            var middleware = new ExceptionHandlingMiddleware(_ => throw error, NullLogger<ExceptionHandlingMiddleware>.Instance);
            await middleware.InvokeAsync(context);
            Check(context.Response.StatusCode == expected, "Incorrect HTTP status");
            context.Response.Body.Position = 0;
            var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
            Check(!body.Contains("QA internal details"), "Internal details exposed");
        }
    });
    await Case("HTTP: authentication, POST 201/400, GET order, persisted stock", async () =>
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(Dsw2025Tpi.Api.Controllers.OrdersController).Assembly);
        builder.Services.AddDbContext<Dsw2025TpiContext>(o => o.UseSqlServer(connectionString));
        builder.Services.AddScoped<IRepository, EfRepository>();
        builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        builder.Services.AddScoped<OrdersManagementService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
            o.TokenValidationParameters = new TokenValidationParameters {
                ValidateIssuer = true, ValidIssuer = "qa", ValidateAudience = true, ValidAudience = "qa",
                ValidateLifetime = true, ValidateIssuerSigningKey = true, IssuerSigningKey = key });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var (customer, products) = await Seed(1);
            var request = Request(customer, (products[0], 1));
            using var anonymous = await client.PostAsJsonAsync("/api/orders", request);
            Check((int)anonymous.StatusCode == 401, "Anonymous order request should return 401");
            var token = new JwtSecurityToken("qa", "qa", expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            using var success = await client.PostAsJsonAsync("/api/orders", request);
            Check((int)success.StatusCode == 201, $"POST failed: {await success.Content.ReadAsStringAsync()}");
            Check(success.Headers.Location != null, "Missing order Location");
            using var get = await client.GetAsync(success.Headers.Location);
            Check((int)get.StatusCode == 200, "Created order cannot be retrieved");
            using var failure = await client.PostAsJsonAsync("/api/orders", request);
            Check((int)failure.StatusCode == 400, "Insufficient stock should return 400");
            await using var db = Context();
            Check((await db.Set<Product>().FindAsync(products[0]))!.StockQuantity == 0, "HTTP stock mismatch");
            Check(await db.Set<Order>().CountAsync(o => o.CustomerId == customer) == 1, "HTTP persisted orders mismatch");
        }
        finally { await app.StopAsync(); }
    });
}
finally
{
    if (created)
    {
        await using var db = Context();
        await db.Database.EnsureDeletedAsync();
        Console.WriteLine("Isolated QA database deleted.");
    }
}
Console.WriteLine($"Failures: {failures}");
return failures == 0 ? 0 : 1;

sealed class ReadHookRepository(IRepository inner, Func<Product, Task>? hook) : IRepository
{
    public async Task<T?> GetById<T>(Guid id, params string[] include) where T : EntityBase
    {
        var entity = await inner.GetById<T>(id, include);
        if (entity is Product product && hook != null) await hook(product);
        return entity;
    }
    public Task<T> Add<T>(T entity) where T : EntityBase => inner.Add(entity);
    public Task<T> Update<T>(T entity) where T : EntityBase => inner.Update(entity);
    public Task<T> Delete<T>(T entity) where T : EntityBase => inner.Delete(entity);
    public Task<T?> First<T>(Expression<Func<T, bool>> predicate, params string[] include) where T : EntityBase => inner.First(predicate, include);
    public Task<IEnumerable<T>> GetAll<T>(params string[] include) where T : EntityBase => inner.GetAll<T>(include);
    public Task<IEnumerable<T>> GetFiltered<T>(Expression<Func<T, bool>> predicate, params string[] include) where T : EntityBase => inner.GetFiltered(predicate, include);
}
