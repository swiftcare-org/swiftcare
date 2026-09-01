using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ApiGateway.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.UnitTests.Routing;

// Boots the real ApiGateway pipeline - including the actual appsettings.json ReverseProxy
// route/AuthorizationPolicy configuration - so these tests catch a wrong policy name or
// route Order that unit tests over the middleware in isolation cannot see, since those
// never touch the JSON configuration at all.
//
// The auth-cluster destination is overridden to an address nothing listens on, rather than
// relying on appsettings.json's http://localhost:5000. A developer machine can easily have
// a real AuthService instance running on 5000 (e.g. from an IDE debug session), and a real
// server that rejects the test's ad-hoc gateway secret would return a real 401 - making the
// test pass or fail depending on what else happens to be running, rather than on whether the
// Gateway itself blocked the request. What matters here is only whether the Gateway rejected
// the request with 401 before ever attempting to forward it.
public sealed class ApiGatewayWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSigningKey = "integration-test-gateway-signing-key-must-be-at-least-32-bytes";
    private const string TestIssuer = "SwiftCare.ApiGateway.Tests";
    private const string TestAudience = "SwiftCare.Tests";
    public const string TestFrontendOrigin = "https://frontend.swiftcare.test";
    public const string TestGatewaySecret = "integration-test-gateway-secret-value";

    public RevokedTokenStore RevokedTokenStore => Services.GetRequiredService<RevokedTokenStore>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = TestSigningKey,
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience,
                ["Gateway:InternalSecret"] = TestGatewaySecret,
                ["Cors:AllowedOrigins:0"] = TestFrontendOrigin,
                // A port nothing binds to in a test environment - see class comment above.
                ["ReverseProxy:Clusters:auth-cluster:Destinations:auth-destination:Address"] = "http://localhost:59999",
                ["ReverseProxy:Clusters:patient-cluster:Destinations:patient-destination:Address"] = "http://localhost:59999"
            });
        });
    }

    // Mints a token with the same signing key/issuer/audience the test host validates
    // against, mirroring AuthService's JwtTokenService claim shape.
    public string CreateSignedToken(string? jti = null, DateTime? expiresAtUtc = null, string? role = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Jti, jti ?? Guid.NewGuid().ToString())
        };

        if (role is not null)
        {
            claims.Add(new Claim("role", role));
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: expiresAtUtc ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
