using System.Text;
using ApiGateway.Middleware;
using ApiGateway.Models;
using ApiGateway.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // AuthService issues tokens under the standard "sub"/"jti" claim names (see
        // JwtTokenService.GenerateToken). Without this, ASP.NET Core's default inbound
        // claim mapping silently rewrites "sub" to a different claim type and every
        // downstream claim lookup here and in TokenRevocationMiddleware returns null.
        options.MapInboundClaims = false;

        // Read from builder.Configuration inside this lambda - not into a local variable
        // above it - because this delegate only runs when JwtBearerOptions is first
        // resolved (per-request, after the app has started), not at registration time.
        // Capturing the values into locals here would freeze them at registration time,
        // before any configuration sources added later (e.g. a test host's in-memory
        // overrides during Build()) have been layered in.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"] ?? string.Empty)),
            ClockSkew = TimeSpan.Zero,

            // JwtTokenService issues the role claim under the raw "role" name, and
            // MapInboundClaims = false above keeps it that way instead of remapping it to
            // the long ClaimTypes.Role URI. RequireRole(...) below resolves through
            // ClaimsIdentity.RoleClaimType, which defaults to that URI - without this,
            // RequireRole("Admin") would find no role claim at all and reject every admin.
            RoleClaimType = "role"
        };

        // Without this, a missing/invalid/expired token produces a bodyless 401 - every
        // other 401 in the login/logout flow (AuthService's GatewaySecretMiddleware and
        // AuthController, and TokenRevocationMiddleware below) returns a MessageResponse
        // body, so this keeps the contract consistent regardless of which layer rejects.
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new MessageResponse("Unauthorized"));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new MessageResponse("Forbidden"));
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Role-based authorization is centralized here at the Gateway; services enforce only
    // resource-level rules (e.g. ownership) on top of the identity headers this forwards.
    options.AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
    options.AddPolicy("ReceptionistOnly", policy => policy.RequireAuthenticatedUser().RequireRole("Receptionist"));
});

builder.Services.AddSingleton<RevokedTokenStore>();

var app = builder.Build();

// Fail fast before serving any request if required configuration is missing. Checked
// against app.Configuration (post-Build) rather than builder.Configuration so that test
// hosts which inject configuration during Build() (e.g. WebApplicationFactory) are
// honored - matching the pattern used by AuthService's own startup checks.
var jwtSecretKey = app.Configuration["Jwt:SecretKey"];
if (string.IsNullOrEmpty(jwtSecretKey) || Encoding.UTF8.GetByteCount(jwtSecretKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey is not configured or is under 32 bytes. Set it via the Jwt__SecretKey environment variable.");
}

if (string.IsNullOrEmpty(app.Configuration["Jwt:Issuer"]))
{
    throw new InvalidOperationException(
        "Jwt:Issuer is not configured. Set it via the Jwt__Issuer environment variable.");
}

if (string.IsNullOrEmpty(app.Configuration["Jwt:Audience"]))
{
    throw new InvalidOperationException(
        "Jwt:Audience is not configured. Set it via the Jwt__Audience environment variable.");
}

var allowedOrigins = app.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? [];

if (allowedOrigins.Length == 0)
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins is not configured. Set it via Cors__AllowedOrigins__0 (and __1, __2 ...).");
}

if (string.IsNullOrEmpty(app.Configuration["Gateway:InternalSecret"]))
{
    throw new InvalidOperationException(
        "Gateway:InternalSecret is not configured. Set it via the Gateway__InternalSecret environment variable.");
}

app.UseCors(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod());

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
