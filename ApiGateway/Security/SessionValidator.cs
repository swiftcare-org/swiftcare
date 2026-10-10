using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Security;

public sealed class SessionValidator(HttpClient client, IProxyConfigProvider proxy, IConfiguration configuration) : ISessionValidator
{
    public async Task<bool> ValidateAsync(ClaimsPrincipal user, bool revoke, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) || userId == Guid.Empty
            || !Guid.TryParse(user.FindFirst("sessionVersion")?.Value, out var version) || version == Guid.Empty
            || !long.TryParse(user.FindFirst(JwtRegisteredClaimNames.Exp)?.Value, out var expiration))
            return false;
        var tokenId = user.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrWhiteSpace(tokenId) || tokenId.Length > 128) return false;
        var cluster = proxy.GetConfig().Clusters.Single(cluster => cluster.ClusterId == "auth-cluster");
        var address = cluster.Destinations?.Values.FirstOrDefault()?.Address
            ?? throw new InvalidOperationException("Session validation destination is unavailable.");
        using var request = new HttpRequestMessage(HttpMethod.Post, address.TrimEnd('/') + "/internal/sessions/validate");
        request.Headers.Add("X-Gateway-Secret", configuration["Gateway:InternalSecret"]);
        request.Content = JsonContent.Create(new { UserId = userId, SessionVersion = version, TokenId = tokenId, ExpiresAtUnixSeconds = expiration, Revoke = revoke });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<bool>(cancellationToken);
    }
}
