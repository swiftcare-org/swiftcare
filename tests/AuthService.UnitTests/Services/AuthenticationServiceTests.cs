using AuthService.Data;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AuthService.UnitTests.Services;

public class AuthenticationServiceTests
{
    private const string ValidPassword = "correct-horse-battery-staple";
    private const string CorrelationId = "test-correlation-id";
    private const string IpAddress = "127.0.0.1";

    private static AuthDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static User CreateUser(
        UserRole role = UserRole.Doctor,
        bool isActive = true,
        bool isDeleted = false,
        string? roomNumber = "R-204") => new()
    {
        Username = "dr.chen",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
        FullName = "Dr. Amara Chen",
        Role = role,
        RoomNumber = roomNumber,
        IsActive = isActive,
        IsDeleted = isDeleted
    };

    private static AuthenticationService CreateService(AuthDbContext dbContext, IJwtTokenService? jwtTokenService = null)
    {
        if (jwtTokenService is null)
        {
            var mock = new Mock<IJwtTokenService>();
            mock.Setup(s => s.GenerateToken(It.IsAny<User>()))
                .Returns(("signed-jwt", DateTime.UtcNow.AddHours(12)));
            jwtTokenService = mock.Object;
        }

        return new AuthenticationService(dbContext, jwtTokenService, NullLogger<AuthenticationService>.Instance);
    }

    [Fact]
    public async Task LoginWithValidCredentialsAndActiveAccountReturnsSuccessWithUserData()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var result = await service.LoginAsync(user.Username, ValidPassword, CorrelationId, IpAddress);

        Assert.Equal(LoginOutcome.Success, result.Outcome);
        Assert.Equal("signed-jwt", result.Token);
        Assert.NotNull(result.User);
        Assert.Equal(user.Id, result.User!.UserId);
        Assert.Equal(user.FullName, result.User.FullName);
        Assert.Equal(user.Role, result.User.Role);
        Assert.Equal(user.RoomNumber, result.User.RoomNumber);
    }

    [Fact]
    public async Task LoginWithWrongPasswordReturnsInvalidCredentialsAndNoToken()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var result = await service.LoginAsync(user.Username, "wrong-password", CorrelationId, IpAddress);

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.Token);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task LoginWithUnknownUsernameReturnsInvalidCredentialsIndistinguishableFromWrongPassword()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var result = await service.LoginAsync("no-such-user", ValidPassword, CorrelationId, IpAddress);

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.Token);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task LoginWithCorrectCredentialsButDeactivatedAccountReturnsAccountDeactivatedAndNoToken()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(isActive: false);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var result = await service.LoginAsync(user.Username, ValidPassword, CorrelationId, IpAddress);

        Assert.Equal(LoginOutcome.AccountDeactivated, result.Outcome);
        Assert.Null(result.Token);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task LoginWithCorrectCredentialsButSoftDeletedAccountIsTreatedAsUnknownUser()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(isDeleted: true);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var result = await service.LoginAsync(user.Username, ValidPassword, CorrelationId, IpAddress);

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.Token);
    }

    [Theory]
    [InlineData(UserRole.Receptionist)]
    [InlineData(UserRole.Admin)]
    public async Task LoginForNonDoctorRoleOmitsRoomNumberFromReturnedUserEvenIfSetInDatabase(UserRole role)
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(role: role, roomNumber: "R-999");
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var result = await service.LoginAsync(user.Username, ValidPassword, CorrelationId, IpAddress);

        Assert.Equal(LoginOutcome.Success, result.Outcome);
        Assert.Null(result.User!.RoomNumber);
    }

    [Fact]
    public async Task EachLoginOutcomeWritesExactlyOneAuditEntryWithMatchingOutcome()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        await service.LoginAsync(user.Username, ValidPassword, CorrelationId, IpAddress);
        await service.LoginAsync(user.Username, "wrong-password", CorrelationId, IpAddress);
        await service.LoginAsync("no-such-user", ValidPassword, CorrelationId, IpAddress);

        var entries = await dbContext.LoginAuditEntries.ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.Single(entries, e => e.Outcome == LoginOutcome.Success && e.UserId == user.Id);
        Assert.Single(entries, e => e.Outcome == LoginOutcome.InvalidCredentials && e.UserId == user.Id);
        Assert.Single(entries, e => e.Outcome == LoginOutcome.InvalidCredentials && e.UserId == null);
    }

    [Fact]
    public async Task LoginNeverPersistsTheAttemptedUsernameOnAFailedAttempt()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await service.LoginAsync("mistyped-password-here", ValidPassword, CorrelationId, IpAddress);

        var entry = Assert.Single(dbContext.LoginAuditEntries);
        Assert.Null(entry.UserId);
        Assert.Equal(LoginOutcome.InvalidCredentials, entry.Outcome);
    }
}
