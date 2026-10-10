using AuthService.Data;
using AuthService.Models.Dtos;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthService.UnitTests.Services;

public sealed class SessionValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly DbContextOptions<AuthDbContext> _options = new DbContextOptionsBuilder<AuthDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private async Task<User> SeedAsync()
    {
        await using var context = new AuthDbContext(_options);
        var user = new User { Username = "test-doctor", FullName = "Test Doctor", PasswordHash = "old-hash", Role = UserRole.Doctor, RoomNumber = "R-1" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static SessionValidationRequest Request(User user, bool revoke = false, string? tokenId = null) =>
        new(user.Id, user.SessionVersion, tokenId ?? Guid.NewGuid().ToString(), Now.AddHours(1).ToUnixTimeSeconds(), revoke);

    private async Task<bool> ValidateAsync(SessionValidationRequest request)
    {
        await using var context = new AuthDbContext(_options);
        return await new SessionValidationService(context, new Clock()).ValidateAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task LogoutPersistsAcrossReplicasAndDoesNotInvalidateOtherTokens()
    {
        var user = await SeedAsync();
        var request = Request(user);
        Assert.True(await ValidateAsync(request));
        Assert.True(await ValidateAsync(request with { Revoke = true }));
        Assert.False(await ValidateAsync(request)); // A fresh context models another replica or a restart.
        Assert.False(await ValidateAsync(request with { Revoke = true }));
        Assert.True(await ValidateAsync(Request(user)));
        await using var reader = new AuthDbContext(_options);
        var stored = Assert.Single(await reader.RevokedSessions.ToListAsync());
        Assert.Equal(request.TokenId, stored.TokenId);
        Assert.Equal(Now.AddHours(1).UtcDateTime, stored.ExpiresAtUtc);
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("password")]
    [InlineData("delete")]
    [InlineData("role")]
    [InlineData("room")]
    public async Task AccountChangesInvalidatePreviouslyIssuedSessions(string change)
    {
        var user = await SeedAsync();
        var old = Request(user);
        await using (var context = new AuthDbContext(_options))
        {
            var stored = await context.Users.SingleAsync();
            switch (change)
            {
                case "deactivate": stored.IsActive = false; break;
                case "password": stored.PasswordHash = "new-hash"; break;
                case "delete": stored.IsDeleted = true; break;
                case "role": stored.Role = UserRole.Receptionist; break;
                default: stored.RoomNumber = "R-2"; break;
            }
            await context.SaveChangesAsync();
            Assert.NotEqual(user.SessionVersion, stored.SessionVersion);
        }
        Assert.False(await ValidateAsync(old));
    }

    [Fact]
    public async Task ReactivationNeverRevivesAnOldSession()
    {
        var user = await SeedAsync();
        var old = Request(user);
        await using (var context = new AuthDbContext(_options))
        {
            var service = new UserAccountService(context, NullLogger<UserAccountService>.Instance);
            var admin = new AdminActionContext(Guid.NewGuid(), "session-test", "127.0.0.1");
            await service.DeactivateUserAsync(user.Id, admin);
            Assert.False(await ValidateAsync(old));
            await service.ReactivateUserAsync(user.Id, admin);
        }
        Assert.False(await ValidateAsync(old));
        await using var reader = new AuthDbContext(_options);
        Assert.True(await ValidateAsync(Request(await reader.Users.SingleAsync())));
    }

    [Fact]
    public async Task PasswordResetThroughTheAccountServiceInvalidatesPriorSessions()
    {
        var user = await SeedAsync();
        var old = Request(user);
        await using (var context = new AuthDbContext(_options))
        {
            var service = new UserAccountService(context, NullLogger<UserAccountService>.Instance);
            await service.ResetPasswordAsync(user.Id, "new-password-123", new AdminActionContext(Guid.NewGuid(), "reset", "127.0.0.1"));
        }
        Assert.False(await ValidateAsync(old));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("version")]
    [InlineData("missingUser")]
    [InlineData("wrongVersion")]
    [InlineData("emptyToken")]
    [InlineData("blankToken")]
    [InlineData("longToken")]
    [InlineData("expired")]
    [InlineData("invalidExpiration")]
    public async Task InvalidSessionsAreRejected(string invalid)
    {
        var user = await SeedAsync();
        var request = Request(user);
        request = invalid switch
        {
            "user" => request with { UserId = Guid.Empty },
            "version" => request with { SessionVersion = Guid.Empty },
            "missingUser" => request with { UserId = Guid.NewGuid() },
            "wrongVersion" => request with { SessionVersion = Guid.NewGuid() },
            "emptyToken" => request with { TokenId = "" },
            "blankToken" => request with { TokenId = " " },
            "longToken" => request with { TokenId = new string('x', 129) },
            "expired" => request with { ExpiresAtUnixSeconds = Now.ToUnixTimeSeconds() },
            _ => request with { ExpiresAtUnixSeconds = long.MaxValue }
        };
        Assert.False(await ValidateAsync(request));
    }

    [Fact]
    public async Task ExpiredRevocationsArePurgedInBoundedBatches()
    {
        var user = await SeedAsync();
        await using (var context = new AuthDbContext(_options))
        {
            context.RevokedSessions.AddRange(Enumerable.Range(0, 501).Select(index =>
                new RevokedSession { TokenId = index.ToString(), ExpiresAtUtc = Now.UtcDateTime }));
            context.RevokedSessions.Add(new RevokedSession { TokenId = "retained", ExpiresAtUtc = Now.AddHours(1).UtcDateTime });
            await context.SaveChangesAsync();
        }
        Assert.True(await ValidateAsync(Request(user)));
        await using var reader = new AuthDbContext(_options);
        Assert.Equal(2, await reader.RevokedSessions.CountAsync());
        Assert.Contains(await reader.RevokedSessions.ToListAsync(), session => session.TokenId == "retained");
        Assert.True(await ValidateAsync(Request(user)));
        reader.ChangeTracker.Clear();
        Assert.Equal("retained", (await reader.RevokedSessions.SingleAsync()).TokenId);
    }

    [Fact]
    public async Task FailedRevocationWriteDoesNotAcknowledgeLogout()
    {
        var user = await SeedAsync();
        var options = new DbContextOptionsBuilder<AuthDbContext>(_options).AddInterceptors(new SaveFailure()).Options;
        await using var context = new AuthDbContext(options);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new SessionValidationService(context, new Clock()).ValidateAsync(Request(user, revoke: true), CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.True(await ValidateAsync(Request(user)));
    }

    private sealed class SaveFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new DbUpdateException("Storage unavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledOrDeletedAccountsCannotUseEvenTheirCurrentVersion(bool deleted)
    {
        await SeedAsync();
        await using var context = new AuthDbContext(_options);
        var user = await context.Users.SingleAsync();
        if (deleted) user.IsDeleted = true;
        else user.IsActive = false;
        await context.SaveChangesAsync();
        Assert.False(await ValidateAsync(Request(user)));
    }

    [Fact]
    public async Task CompetingLogoutDoesNotAcknowledgeAnAlreadyRevokedToken()
    {
        var user = await SeedAsync();
        var request = Request(user, revoke: true);
        var options = new DbContextOptionsBuilder<AuthDbContext>(_options)
            .AddInterceptors(new CompetingLogout(_options, request)).Options;
        await using var context = new AuthDbContext(options);
        Assert.False(await new SessionValidationService(context, new Clock()).ValidateAsync(request, CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.False(await ValidateAsync(request with { Revoke = false }));
    }

    [Fact]
    public async Task ConcurrentExpiryCleanupDoesNotRejectAnOtherwiseValidSession()
    {
        var user = await SeedAsync();
        await using (var context = new AuthDbContext(_options))
        {
            context.RevokedSessions.Add(new RevokedSession { TokenId = "expired", ExpiresAtUtc = Now.UtcDateTime });
            await context.SaveChangesAsync();
        }
        var options = new DbContextOptionsBuilder<AuthDbContext>(_options).AddInterceptors(new CompetingCleanup(_options)).Options;
        await using var reader = new AuthDbContext(options);
        Assert.True(await new SessionValidationService(reader, new Clock()).ValidateAsync(Request(user), CancellationToken.None));
        Assert.Empty(await reader.RevokedSessions.ToListAsync());
        Assert.Empty(reader.ChangeTracker.Entries());
    }

    private sealed class CompetingLogout(DbContextOptions<AuthDbContext> options, SessionValidationRequest request) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await using var other = new AuthDbContext(options);
            other.RevokedSessions.Add(new RevokedSession { TokenId = request.TokenId,
                ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(request.ExpiresAtUnixSeconds).UtcDateTime });
            await other.SaveChangesAsync(cancellationToken);
            throw new DbUpdateException("A competing logout committed first");
        }
    }

    private sealed class CompetingCleanup(DbContextOptions<AuthDbContext> options) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await using var other = new AuthDbContext(options);
            other.RevokedSessions.RemoveRange(await other.RevokedSessions.ToListAsync(cancellationToken));
            await other.SaveChangesAsync(cancellationToken);
            throw new DbUpdateConcurrencyException("Expired rows were already removed");
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
