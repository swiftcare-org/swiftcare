using System.Security.Cryptography;
using System.Text;
using NotificationService.Logging;
using NotificationService.Models.Dtos;

namespace NotificationService.Middleware;

// Services trust the Gateway, not the client. Every request except the public paths must
// carry the shared X-Gateway-Secret header, proving the API Gateway forwarded it.
public sealed class GatewaySecretMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<GatewaySecretMiddleware> logger)
{
    public const string HeaderName = "X-Gateway-Secret";

    // /openapi and /scalar are only mapped in Development, so exempting them has no
    // effect in Production.
    private static readonly string[] PublicPrefixes = ["/openapi", "/scalar"];

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        return IsPublic(request.Path) || CarriesTheSecret(request)
            ? next(context)
            : RejectAsync(context);
    }

    private static bool IsPublic(PathString path) =>
        path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || PublicPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private bool CarriesTheSecret(HttpRequest request)
    {
        var expected = configuration["Gateway:InternalSecret"];
        if (string.IsNullOrEmpty(expected))
        {
            return false;
        }

        // FixedTimeEquals takes the same time whatever the content, so response timing
        // cannot be used to guess the secret. Values of different lengths never match.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(request.Headers[HeaderName].ToString()));
    }

    private Task RejectAsync(HttpContext context)
    {
        // The path is client-supplied: without stripping CR/LF a crafted path could
        // forge extra log lines.
        logger.LogWarning(
            "Rejected request without a valid gateway secret: path={Path}",
            LogSanitizer.Sanitize(context.Request.Path.Value));

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(new MessageResponse("Unauthorized"), context.RequestAborted);
    }
}
