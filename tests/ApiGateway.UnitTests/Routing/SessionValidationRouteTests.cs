using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using ApiGateway.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApiGateway.UnitTests.Routing;

public sealed class SessionValidationRouteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrUnavailableSessionIsBlockedByTheRealPipeline(bool unavailable)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISessionValidator>();
            services.AddSingleton<ISessionValidator>(new InvalidSession(unavailable));
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateSignedToken(role: "Doctor"));
        using var response = await client.GetAsync("/api/queue/today");
        Assert.Equal(unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class InvalidSession(bool unavailable) : ISessionValidator
    {
        public Task<bool> ValidateAsync(ClaimsPrincipal user, bool revoke, CancellationToken cancellationToken) =>
            unavailable ? Task.FromException<bool>(new HttpRequestException()) : Task.FromResult(false);
    }
}
