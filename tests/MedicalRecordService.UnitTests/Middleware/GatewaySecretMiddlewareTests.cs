using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MedicalRecordService.Middleware;

namespace MedicalRecordService.UnitTests.Middleware;

public class GatewaySecretMiddlewareTests
{
    private const string Secret = "gateway-secret-value";

    [Fact]
    public async Task InvokeAsync_CallsNext_WhenSecretMatches()
    {
        var result = await InvokeAsync("/api/resource", providedSecret: Secret);

        Assert.True(result.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, result.Context.Response.StatusCode);
    }

    [Theory]
    [InlineData("Doctor", "11111111-1111-1111-1111-111111111111")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public async Task ValidGatewayEstablishesOnlyProvidedIdentityClaims(string? role, string? userId)
    {
        var result = await InvokeAsync("/api/resource", Secret, userId: userId, role: role);

        Assert.True(result.NextCalled);
        Assert.True(result.Context.User.Identity!.IsAuthenticated);
        Assert.Equal("Gateway", result.Context.User.Identity.AuthenticationType);
        Assert.Equal(string.IsNullOrEmpty(userId) ? null : userId, result.Context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(string.IsNullOrEmpty(role) ? null : role, result.Context.User.FindFirstValue(ClaimTypes.Role));
        Assert.Equal(role == "Doctor", result.Context.User.IsInRole("Doctor"));
    }

    [Fact]
    public async Task PublicPathDoesNotEstablishIdentityFromHeaders()
    {
        var result = await InvokeAsync("/health", null, userId: Guid.NewGuid().ToString(), role: "Doctor");

        Assert.True(result.NextCalled);
        Assert.False(result.Context.User.IsInRole("Doctor"));
        Assert.False(result.Context.User.Identity?.IsAuthenticated == true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gateway-secret-valuX")]
    [InlineData("gateway-secret")]
    [InlineData("gateway-secret-value-longer")]
    public async Task InvokeAsync_Returns401_WhenProvidedSecretIsMissingOrWrong(string? providedSecret)
    {
        var result = await InvokeAsync("/api/resource", providedSecret);

        await AssertUnauthorizedAsync(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task InvokeAsync_Returns401_WhenGatewaySecretIsNotConfigured(string? configuredSecret)
    {
        var result = await InvokeAsync("/api/resource", providedSecret: Secret, configuredSecret);

        await AssertUnauthorizedAsync(result);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/HEALTH")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task InvokeAsync_SkipsSecretCheck_ForPublicPaths(string path)
    {
        var result = await InvokeAsync(path, providedSecret: null);

        Assert.True(result.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, result.Context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/health/details")]
    [InlineData("/openapi-docs")]
    [InlineData("/api/health")]
    public async Task InvokeAsync_RequiresSecret_ForPathsThatOnlyResemblePublicPaths(string path)
    {
        var result = await InvokeAsync(path, providedSecret: null);

        await AssertUnauthorizedAsync(result);
    }

    private static async Task<InvocationResult> InvokeAsync(
        string path,
        string? providedSecret,
        string? configuredSecret = Secret,
        string? userId = null,
        string? role = null)
    {
        var nextCalled = false;
        var middleware = new GatewaySecretMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<GatewaySecretMiddleware>.Instance);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:InternalSecret"] = configuredSecret
            })
            .Build();

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (userId is not null) context.Request.Headers["X-User-Id"] = userId;
        if (role is not null) context.Request.Headers["X-User-Role"] = role;
        if (providedSecret is not null)
        {
            context.Request.Headers["X-Gateway-Secret"] = providedSecret;
        }

        await middleware.InvokeAsync(context, configuration);

        return new InvocationResult(context, nextCalled);
    }

    private static async Task AssertUnauthorizedAsync(InvocationResult result)
    {
        Assert.False(result.NextCalled);
        Assert.False(result.Context.User.Identity?.IsAuthenticated == true);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.Context.Response.StatusCode);

        result.Context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(result.Context.Response.Body);
        Assert.Equal("Unauthorized", body.RootElement.GetProperty("message").GetString());
    }

    private sealed record InvocationResult(DefaultHttpContext Context, bool NextCalled);
}
