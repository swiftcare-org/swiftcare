namespace ApiGateway.Middleware;

// Enforces the trust boundary between the client and the backend services: a client
// must never be able to forge identity or bypass the gateway secret by sending these
// headers itself, so they are stripped from every incoming request before the trusted,
// gateway-issued X-Gateway-Secret is attached for the proxied call.
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

        await _next(context);
    }
}
