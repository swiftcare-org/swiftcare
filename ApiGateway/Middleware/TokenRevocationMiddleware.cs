using System.IdentityModel.Tokens.Jwt;
using ApiGateway.Models;
using ApiGateway.Security;

namespace ApiGateway.Middleware;

// Runs after JWT signature/claims validation. Enforces the revocation denylist so a
// logged-out token is rejected immediately rather than remaining valid until it expires,
// and revokes the token being used for a logout request itself.
//
// Revocation happens on the way in, before the request is forwarded to AuthService, so
// that logout always terminates the session locally even if AuthService is unreachable
// or the audit write fails. The token is burned unconditionally; the audit record is not.
public sealed class TokenRevocationMiddleware
{
    private const string LogoutPath = "/api/auth/logout";

    private readonly RequestDelegate _next;
    private readonly RevokedTokenStore _revokedTokenStore;
    private readonly ILogger<TokenRevocationMiddleware> _logger;

    public TokenRevocationMiddleware(
        RequestDelegate next,
        RevokedTokenStore revokedTokenStore,
        ILogger<TokenRevocationMiddleware> logger)
    {
        _next = next;
        _revokedTokenStore = revokedTokenStore;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var jti = context.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrEmpty(jti))
        {
            _logger.LogWarning("Rejected authenticated request with no jti claim: userId={UserId}", userId);
            await WriteUnauthorizedAsync(context);
            return;
        }

        // Checked before the logout revoke-write below, so a replay of an already-revoked
        // token - including a second logout call with the same token - is rejected rather
        // than silently re-revoking an already-dead entry.
        if (_revokedTokenStore.IsRevoked(jti))
        {
            _logger.LogWarning("Rejected revoked token: userId={UserId}", userId);
            await WriteUnauthorizedAsync(context);
            return;
        }

        if (IsLogoutRequest(context))
        {
            var expClaim = context.User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
            var expiresAtUtc = long.TryParse(expClaim, out var expUnixSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(expUnixSeconds)
                : DateTimeOffset.UtcNow; // Malformed/missing exp: don't retain the entry past "now".

            _revokedTokenStore.Revoke(jti, expiresAtUtc);
            _logger.LogInformation("Revoked token on logout: userId={UserId}", userId);
        }

        await _next(context);
    }

    private static bool IsLogoutRequest(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Equals(LogoutPath, StringComparison.OrdinalIgnoreCase);

    // Matches the MessageResponse contract used everywhere else a 401 is returned in the
    // login/logout flow (AuthService's GatewaySecretMiddleware/AuthController, and this
    // Gateway's own JwtBearer OnChallenge handler for missing/invalid/expired tokens).
    private static Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(new MessageResponse("Unauthorized"));
    }
}
