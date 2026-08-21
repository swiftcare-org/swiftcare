using System.Text;
using ApiGateway.Middleware;
using ApiGateway.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

const string FrontendCorsPolicy = "FrontendCorsPolicy";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options =>
{
    // The React frontend (port 5173) is the only client permitted to call the Gateway directly.
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var jwtSecretKey = builder.Configuration["Jwt:SecretKey"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // AuthService issues tokens under the standard "sub"/"jti" claim names (see
        // JwtTokenService.GenerateToken). Without this, ASP.NET Core's default inbound
        // claim mapping silently rewrites "sub" to a different claim type and every
        // downstream claim lookup here and in TokenRevocationMiddleware returns null.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey ?? string.Empty)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<RevokedTokenStore>();

var app = builder.Build();

// Fail fast before serving any request if required configuration is missing, matching
// the pattern used by AuthService's own startup checks.
if (string.IsNullOrEmpty(jwtSecretKey) || Encoding.UTF8.GetByteCount(jwtSecretKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey is not configured or is under 32 bytes. Set it via the Jwt__SecretKey environment variable.");
}

if (string.IsNullOrEmpty(jwtIssuer))
{
    throw new InvalidOperationException(
        "Jwt:Issuer is not configured. Set it via the Jwt__Issuer environment variable.");
}

if (string.IsNullOrEmpty(jwtAudience))
{
    throw new InvalidOperationException(
        "Jwt:Audience is not configured. Set it via the Jwt__Audience environment variable.");
}

if (string.IsNullOrEmpty(app.Configuration["Gateway:InternalSecret"]))
{
    throw new InvalidOperationException(
        "Gateway:InternalSecret is not configured. Set it via the Gateway__InternalSecret environment variable.");
}

app.UseCors(FrontendCorsPolicy);

app.UseMiddleware<CorrelationIdMiddleware>();

// Routing must run explicitly (rather than relying on auto-insertion) so that
// UseAuthorization below reliably sees the YARP endpoint's AuthorizationPolicy metadata.
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<TokenRevocationMiddleware>();
app.UseAuthorization();

app.UseMiddleware<GatewayForwardingMiddleware>();

app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();

public partial class Program;
