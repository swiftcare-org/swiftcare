using System.Security.Cryptography;
using System.Text;
using AuthService.Models.Dtos;

namespace AuthService.Middleware;

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
        if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
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
            _logger.LogWarning("Rejected request without a valid gateway secret: path={Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new MessageResponse("Unauthorized"));
            return;
        }

        await _next(context);
    }

    // Constant-time comparison so response timing cannot be used to brute-force the secret.
    private static bool SecretsMatch(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
