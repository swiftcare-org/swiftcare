using AuthService.Data;
using AuthService.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace AuthService.UnitTests.Controllers;

// Boots the real ASP.NET Core pipeline (routing, model validation, GatewaySecretMiddleware)
// so controller tests exercise the actual request path instead of calling the action in isolation.
// The database is swapped for EF Core InMemory and IAuthenticationService is swapped for a mock,
// since AuthenticationService's own behavior is already covered by AuthenticationServiceTests.
public sealed class AuthServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ValidGatewaySecret = "integration-test-gateway-secret-value";

    public Mock<IAuthenticationService> AuthenticationServiceMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuthDb"] = "Server=localhost;Database=unused;User=unused;Password=unused;",
                ["Jwt:SecretKey"] = "integration-test-signing-key-must-be-at-least-32-bytes",
                ["Jwt:Issuer"] = "SwiftCare.AuthService.Tests",
                ["Jwt:Audience"] = "SwiftCare.Tests",
                ["Gateway:InternalSecret"] = ValidGatewaySecret
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

            services.RemoveAll<IAuthenticationService>();
            services.AddScoped(_ => AuthenticationServiceMock.Object);
        });
    }
}
