using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PrescriptionService.Middleware;

namespace PrescriptionService.UnitTests.Middleware;

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
        string? configuredSecret = Secret)
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
        Assert.Equal(StatusCodes.Status401Unauthorized, result.Context.Response.StatusCode);

        result.Context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(result.Context.Response.Body);
        Assert.Equal("Unauthorized", body.RootElement.GetProperty("message").GetString());
    }

    private sealed record InvocationResult(DefaultHttpContext Context, bool NextCalled);
}
