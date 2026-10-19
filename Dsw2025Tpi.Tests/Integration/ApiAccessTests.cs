using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dsw2025Tpi.Tests.Support;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Dsw2025Tpi.Tests.Integration;

[Trait("Category", "Integration")]
public class ApiAccessTests
{
    [Theory]
    [InlineData(null, "GET", "/api/products", 200)]
    [InlineData(null, "GET", "/api/orders", 401)]
    [InlineData("User", "GET", "/api/orders", 200)]
    [InlineData("Admin", "GET", "/api/orders", 200)]
    [InlineData(null, "POST", "/api/products", 401)]
    [InlineData("User", "POST", "/api/products", 403)]
    [InlineData("Admin", "POST", "/api/products", 201)]
    [InlineData(null, "PUT", "/api/orders/00000000-0000-0000-0000-000000000001/status", 401)]
    [InlineData("User", "PUT", "/api/orders/00000000-0000-0000-0000-000000000001/status", 403)]
    [InlineData("Admin", "PUT", "/api/orders/00000000-0000-0000-0000-000000000001/status", 404)]
    public async Task ProtectedEndpoints_WithDifferentRoles_ReturnExpectedStatus(
        string? role, string method, string path, int expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient(role);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(ProductRequestFactory.Create());
        if (method == "PUT") request.Content = JsonContent.Create("PROCESSING");

        using var response = await client.SendAsync(request);

        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    public async Task GetOrders_WithInvalidToken_ReturnsUnauthorized(string invalidPart)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient(null);
        var jwt = new JwtSecurityToken(
            issuer: invalidPart == "issuer" ? "wrong" : ApiFactory.Issuer,
            audience: invalidPart == "audience" ? "wrong" : ApiFactory.Audience,
            claims: [new Claim(ClaimTypes.Role, "User"), new Claim("customerId", factory.CustomerId.ToString())],
            notBefore: DateTime.UtcNow.AddHours(-1),
            expires: invalidPart == "expired" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                invalidPart == "signature" ? "WRONG_TEST_KEY_0123456789_ABCDEFGHIJK" : ApiFactory.Key)), SecurityAlgorithms.HmacSha256));
        var token = invalidPart == "malformed" ? "not-a-jwt" : new JwtSecurityTokenHandler().WriteToken(jwt);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterAndLogin_WithValidCredentials_ReturnUsableCustomerTokens()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient(null);
        var registration = new { Username = "new-user", Email = "new@example.test", Password = "Test_Password123!" };

        using var registered = await client.PostAsJsonAsync("/api/auth/register", registration);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var registrationBody = await registered.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = registrationBody.GetProperty("customerId").GetGuid();
        Assert.NotEqual(Guid.Empty, customerId);
        factory.Inspect(db => Assert.Contains(db.Set<Dsw2025Tpi.Domain.Entities.Customer>(), c => c.Id == customerId));

        using var login = await client.PostAsJsonAsync("/api/auth/login", new { registration.Username, registration.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("token").GetString();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(customerId.ToString(), jwt.Claims.Single(c => c.Type == "customerId").Value);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var orders = await client.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.OK, orders.StatusCode);

        using var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { registration.Username, Password = "Wrong123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        using var duplicate = await client.PostAsJsonAsync("/api/auth/register", registration);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownUser_ReturnsUnauthorized()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient(null);

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = "missing", Password = "Test123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(false, 400)]
    [InlineData(true, 409)]
    public async Task AddProduct_WithInvalidOrDuplicateData_ReturnsJsonError(bool duplicate, int expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient("Admin");
        var request = duplicate
            ? ProductRequestFactory.Create(sku: "SKU-1")
            : ProductRequestFactory.Create(currentUnitPrice: -1m);

        using var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("Message").GetString()));
        factory.Inspect(db => Assert.Equal(2, db.Set<Dsw2025Tpi.Domain.Entities.Product>().Count()));
    }

    [Fact]
    public async Task CreateOrder_WithMalformedBody_ReturnsBadRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateSeededClient();
        using var content = new StringContent("{invalid", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/orders", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.Inspect(db => Assert.Empty(db.Set<Dsw2025Tpi.Domain.Entities.Order>()));
    }
}
