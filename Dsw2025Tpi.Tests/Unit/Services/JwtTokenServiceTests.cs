using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Dsw2025Tpi.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Dsw2025Tpi.Tests.Unit.Services;

public class JwtTokenServiceTests
{
    private const string TestKey = "TEST_ONLY_JWT_KEY_0123456789_ABCDEF";
    private const string TestIssuer = "at-b03-tests-issuer";
    private const string TestAudience = "at-b03-tests-audience";

    [Theory]
    [InlineData("Admin")]
    [InlineData("User")]
    [Trait("Category", "Unit")]
    public void GenerateToken_WithValidConfiguration_ReturnsSignedTokenWithExpectedClaims(string role)
    {
        var configuration = CreateConfiguration();
        var service = CreateService(configuration);
        var beforeGeneration = DateTime.UtcNow;

        var token = service.GenerateToken("test-user", role, "customer-123");
        var afterGeneration = DateTime.UtcNow;

        Assert.False(string.IsNullOrWhiteSpace(token));

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TestIssuer,
            ValidateAudience = true,
            ValidAudience = TestAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero
        }, out var validatedToken);

        var jwt = Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Equal(TestIssuer, jwt.Issuer);
        Assert.Contains(TestAudience, jwt.Audiences);
        Assert.Equal("test-user", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(role, principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal("customer-123", principal.FindFirst("customerId")?.Value);
        Assert.True(Guid.TryParse(
            principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value,
            out _));
        Assert.InRange(
            jwt.ValidTo,
            beforeGeneration.AddMinutes(30).AddSeconds(-1),
            afterGeneration.AddMinutes(30).AddSeconds(1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateToken_WithMissingKey_ThrowsArgumentNullException()
    {
        var service = CreateService(CreateConfiguration(includeKey: false));

        Assert.Throws<ArgumentNullException>(() =>
            service.GenerateToken("test-user", "User", "customer-123"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateToken_WithSameIdentityTwice_UsesUniqueGuidJti()
    {
        var service = CreateService(CreateConfiguration());

        var firstToken = new JwtSecurityTokenHandler().ReadJwtToken(
            service.GenerateToken("test-user", "User", "customer-123"));
        var secondToken = new JwtSecurityTokenHandler().ReadJwtToken(
            service.GenerateToken("test-user", "User", "customer-123"));
        var firstJti = firstToken.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Jti).Value;
        var secondJti = secondToken.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Jti).Value;

        Assert.True(Guid.TryParse(firstJti, out _));
        Assert.True(Guid.TryParse(secondJti, out _));
        Assert.NotEqual(firstJti, secondJti);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateToken_WithoutConfiguredExpiration_UsesSixtyMinutes()
    {
        var service = CreateService(CreateConfiguration(expirationMinutes: null));
        var beforeGeneration = DateTime.UtcNow;

        var token = service.GenerateToken("test-user", "User", "customer-123");

        var afterGeneration = DateTime.UtcNow;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.InRange(
            jwt.ValidTo,
            beforeGeneration.AddMinutes(60).AddSeconds(-1),
            afterGeneration.AddMinutes(60).AddSeconds(1));
    }

    private static JwtTokenService CreateService(IConfiguration configuration)
    {
        return new JwtTokenService(
            configuration,
            NullLogger<JwtTokenService>.Instance);
    }

    private static IConfiguration CreateConfiguration(
        string? expirationMinutes = "30",
        bool includeKey = true)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = TestIssuer,
            ["Jwt:Audience"] = TestAudience
        };

        if (includeKey)
        {
            values["Jwt:Key"] = TestKey;
        }

        if (expirationMinutes is not null)
        {
            values["Jwt:ExpireInMinutes"] = expirationMinutes;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
