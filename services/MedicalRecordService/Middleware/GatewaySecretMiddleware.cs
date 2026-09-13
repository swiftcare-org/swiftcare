using System.Security.Cryptography;
using System.Text;
using MedicalRecordService.Logging;
using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.Middleware;

// MedicalRecordService trusts identity only after the API Gateway has authenticated the
// request and attached the shared secret. Client-supplied identity headers are stripped
// and rebuilt by the Gateway before a request reaches this middleware.
public sealed class GatewaySecretMiddleware
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";

    private readonly RequestDelegate _next;
    private readonly ILogger<GatewaySecretMiddleware> _logger;

    public GatewaySecretMiddleware(
        RequestDelegate next,
        ILogger<GatewaySecretMiddleware> logger)
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

        if (string.IsNullOrEmpty(expectedSecret)
            || string.IsNullOrEmpty(providedSecret)
            || !SecretsMatch(expectedSecret, providedSecret))
        {
            _logger.LogWarning(
                "Rejected request without a valid gateway secret: path={Path}",
                LogSanitizer.Sanitize(context.Request.Path.Value));
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new MessageResponse("Unauthorized"));
            return;
        }

        await _next(context);
    }

    private static bool IsUnauthenticatedPath(PathString path) =>
        path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase);

    private static bool SecretsMatch(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
