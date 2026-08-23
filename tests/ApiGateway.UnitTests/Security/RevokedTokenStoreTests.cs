using ApiGateway.Security;

namespace ApiGateway.UnitTests.Security;

public class RevokedTokenStoreTests
{
    [Fact]
    public void IsRevokedReturnsFalseForAJtiThatWasNeverRevoked()
    {
        var store = new RevokedTokenStore();

        Assert.False(store.IsRevoked("never-revoked-jti"));
    }

    [Fact]
    public void RevokeMakesIsRevokedReturnTrueForThatJti()
    {
        var store = new RevokedTokenStore();

        store.Revoke("revoked-jti", DateTimeOffset.UtcNow.AddHours(1));

        Assert.True(store.IsRevoked("revoked-jti"));
    }

    [Fact]
    public void RevokeDoesNotAffectOtherJtis()
    {
        var store = new RevokedTokenStore();

        store.Revoke("revoked-jti", DateTimeOffset.UtcNow.AddHours(1));

        Assert.False(store.IsRevoked("some-other-jti"));
    }

    [Fact]
    public void EntryPastItsTokenExpiryIsEvictedOnTheNextRevokeCall()
    {
        var store = new RevokedTokenStore();
        store.Revoke("already-expired-jti", DateTimeOffset.UtcNow.AddSeconds(-1));

        // Eviction is opportunistic, swept on the next write - trigger it with an unrelated revoke.
        store.Revoke("trigger-sweep-jti", DateTimeOffset.UtcNow.AddHours(1));

        Assert.False(store.IsRevoked("already-expired-jti"));
    }
}
