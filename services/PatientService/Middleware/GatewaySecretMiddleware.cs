using System.Security.Cryptography;
using System.Text;
using PatientService.Logging;
using PatientService.Models.Dtos;

namespace PatientService.Middleware;

// Services trust the Gateway, not the client. Every non-health request must carry
// the shared X-Gateway-Secret header, proving it was forwarded by the API Gateway
// rather than reaching this service directly.
public sealed class GatewaySecretMiddleware
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";

    private readonly RequestDelegate _next;
    private readonly ILogger<GatewaySecretMiddleware> _logger;

    public GatewaySecretMiddleware(RequestDelegate next, ILogger<GatewaySecretMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        if (IsUnauthenticatedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var expectedSecret = configuration["Gateway:InternalSecret"];
        var providedSecret = context.Request.Headers[GatewaySecretHeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(expectedSecret) ||
            string.IsNullOrEmpty(providedSecret) ||
            !SecretsMatch(expectedSecret, providedSecret))
        {
            // Sanitized because the request path is client-supplied: without stripping
            // CR/LF here, a crafted path could forge additional, fake log lines.
            _logger.LogWarning(
                "Rejected request without a valid gateway secret: path={Path}",
                LogSanitizer.Sanitize(context.Request.Path.Value));
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new MessageResponse("Unauthorized"));
            return;
        }

        await _next(context);
    }

    // /openapi and /scalar are only ever mapped inside the Development environment guard in
    // Program.cs, so this exemption has no effect in Production - there is nothing there to
    // exempt. Without it, the interactive API docs would need a hand-crafted
    // X-Gateway-Secret header on every request, making them unusable from a plain browser tab.
    private static bool IsUnauthenticatedPath(PathString path) =>
        path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase);

    // Constant-time comparison so response timing cannot be used to brute-force the secret.
    private static bool SecretsMatch(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
