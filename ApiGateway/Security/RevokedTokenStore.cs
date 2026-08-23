using System.Collections.Concurrent;

namespace ApiGateway.Security;

// Tracks JWTs revoked by logout so a token can be rejected immediately instead of
// waiting out its remaining lifetime. In-memory and per-instance: it does not survive
// a Gateway restart and is not shared across multiple Gateway instances. Acceptable for
// Sprint 1 (single Gateway instance); a distributed store (e.g. Redis) is required before
// scaling the Gateway horizontally.
public sealed class RevokedTokenStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedJtiExpiry = new();

    public void Revoke(string jti, DateTimeOffset tokenExpiresAtUtc)
    {
        _revokedJtiExpiry[jti] = tokenExpiresAtUtc;
        EvictExpiredEntries();
    }

    public bool IsRevoked(string jti) => _revokedJtiExpiry.ContainsKey(jti);

    // A revoked entry only needs to live as long as its token could otherwise have been
    // presented. Once the token itself has expired, signature validation already rejects
    // it, so keeping the entry around is pure memory growth with no security benefit.
    private void EvictExpiredEntries()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (jti, expiresAtUtc) in _revokedJtiExpiry)
        {
            if (expiresAtUtc <= now)
            {
                _revokedJtiExpiry.TryRemove(jti, out _);
            }
        }
    }
}
