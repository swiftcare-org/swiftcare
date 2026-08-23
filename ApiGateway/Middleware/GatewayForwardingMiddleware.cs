using System.IdentityModel.Tokens.Jwt;

namespace ApiGateway.Middleware;

// Enforces the trust boundary between the client and the backend services: a client
// must never be able to forge identity or bypass the gateway secret by sending these
// headers itself, so they are stripped from every incoming request before the trusted,
// gateway-issued X-Gateway-Secret - and, for authenticated requests, the identity headers
// derived from the validated JWT - are attached for the proxied call.
public sealed class GatewayForwardingMiddleware
{
    private static readonly string[] UntrustedHeaderNames =
    [
        "X-Gateway-Secret",
        "X-User-Id",
        "X-User-Role",
        "X-User-Name",
        "X-Room-Number"
    ];

    private readonly RequestDelegate _next;
    private readonly string _gatewayInternalSecret;

    public GatewayForwardingMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _gatewayInternalSecret = configuration["Gateway:InternalSecret"]
            ?? throw new InvalidOperationException(
                "Gateway:InternalSecret is not configured. Set it via the Gateway__InternalSecret environment variable.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        foreach (var headerName in UntrustedHeaderNames)
        {
            context.Request.Headers.Remove(headerName);
        }

        context.Request.Headers["X-Gateway-Secret"] = _gatewayInternalSecret;

        if (context.User.Identity?.IsAuthenticated == true)
        {
            AttachIdentityHeaders(context);
        }

        await _next(context);
    }

    // Only ever reads claims off the principal JWT-bearer already validated the signature
    // and lifetime for - never off client-supplied headers, which were just stripped above.
    private static void AttachIdentityHeaders(HttpContext context)
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var role = context.User.FindFirst("role")?.Value;
        var fullName = context.User.FindFirst("fullName")?.Value;
        var roomNumber = context.User.FindFirst("roomNumber")?.Value;

        if (userId is not null)
        {
            context.Request.Headers["X-User-Id"] = userId;
        }

        if (role is not null)
        {
            context.Request.Headers["X-User-Role"] = role;
        }

        if (fullName is not null)
        {
            context.Request.Headers["X-User-Name"] = fullName;
        }

        // roomNumber is only present on Doctor tokens - see JwtTokenService.GenerateToken.
        if (roomNumber is not null)
        {
            context.Request.Headers["X-Room-Number"] = roomNumber;
        }
    }
}
