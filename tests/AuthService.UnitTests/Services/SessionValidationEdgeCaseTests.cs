using AuthService.Data;
using AuthService.Models.Dtos;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;

namespace AuthService.UnitTests.Services;

// The token ID length boundary, and a malformed request rejected before any database work
// (SWC-151 mutation testing).
public sealed class SessionValidationEdgeCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly DbContextOptions<AuthDbContext> _options = new DbContextOptionsBuilder<AuthDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    [Fact]
    public async Task TokenIdOfExactlyTheMaximumLengthIsAccepted()
    {
        var user = await SeedUserAsync();

        Assert.True(await ValidateAsync(Request(user, tokenId: new string('x', 128))));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("version")]
    public async Task RequestWithAnEmptyIdentifierIsRejectedBeforeExpiredSessionsAreCleanedUp(string empty)
    {
        var user = await SeedUserAsync();
        await using (var context = new AuthDbContext(_options))
        {
            context.RevokedSessions.Add(new RevokedSession { TokenId = "expired", ExpiresAtUtc = Now.UtcDateTime.AddHours(-1) });
            await context.SaveChangesAsync();
        }

        var request = Request(user);
        request = empty == "user" ? request with { UserId = Guid.Empty } : request with { SessionVersion = Guid.Empty };

        Assert.False(await ValidateAsync(request));
        await using var reader = new AuthDbContext(_options);
        Assert.Equal("expired", (await reader.RevokedSessions.SingleAsync()).TokenId);
    }

    private async Task<User> SeedUserAsync()
    {
        await using var context = new AuthDbContext(_options);
        var user = new User
        {
            Username = "test-doctor",
            FullName = "Test Doctor",
            PasswordHash = "hash",
            Role = UserRole.Doctor,
            RoomNumber = "R-1"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static SessionValidationRequest Request(User user, string? tokenId = null) =>
        new(user.Id, user.SessionVersion, tokenId ?? Guid.NewGuid().ToString(), Now.AddHours(1).ToUnixTimeSeconds(), false);

    private async Task<bool> ValidateAsync(SessionValidationRequest request)
    {
        await using var context = new AuthDbContext(_options);
        return await new SessionValidationService(context, new Clock()).ValidateAsync(request, CancellationToken.None);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
