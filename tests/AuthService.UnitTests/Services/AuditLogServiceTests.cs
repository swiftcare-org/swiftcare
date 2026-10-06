using AuthService.Data;
using AuthService.Models.Dtos;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AuthService.UnitTests.Services;

// SWC-138: the audit log lists sign-in, sign-out and admin events, newest first.
public class AuditLogServiceTests
{
    private const string Password = "correct-horse-battery-staple";
    private static readonly DateTime BaseTime = new(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);

    private static AuthDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<User> SeedUserAsync(
        AuthDbContext dbContext, string username, UserRole role = UserRole.Admin, bool isDeleted = false)
    {
        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4),
            FullName = username,
            Role = role,
            RoomNumber = role == UserRole.Doctor ? "R-204" : null,
            IsDeleted = isDeleted
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private static LoginAuditEntry Login(Guid? userId, LoginOutcome outcome, int minute, string ip = "10.0.0.1") => new()
    {
        UserId = userId,
        Outcome = outcome,
        CorrelationId = "correlation",
        IpAddress = ip,
        OccurredAt = BaseTime.AddMinutes(minute)
    };

    private static LogoutAuditEntry Logout(Guid userId, int minute) => new()
    {
        UserId = userId,
        CorrelationId = "correlation",
        IpAddress = "10.0.0.2",
        OccurredAt = BaseTime.AddMinutes(minute)
    };

    private static AdminAuditEntry AdminAction(Guid actorId, Guid targetId, AdminAuditAction action, int minute) => new()
    {
        ActorUserId = actorId,
        TargetUserId = targetId,
        Action = action,
        CorrelationId = "correlation",
        IpAddress = "10.0.0.3",
        OccurredAt = BaseTime.AddMinutes(minute)
    };

    [Fact]
    public async Task EntriesFromAllThreeSourcesAreMergedNewestFirst()
    {
        await using var dbContext = CreateDbContext();
        var admin = await SeedUserAsync(dbContext, "admin");
        var doctor = await SeedUserAsync(dbContext, "dr.chen", UserRole.Doctor);
        dbContext.LoginAuditEntries.Add(Login(admin.Id, LoginOutcome.Success, minute: 1));
        dbContext.AdminAuditEntries.Add(AdminAction(admin.Id, doctor.Id, AdminAuditAction.UserDeactivated, minute: 2));
        dbContext.LogoutAuditEntries.Add(Logout(admin.Id, minute: 3));
        await dbContext.SaveChangesAsync();

        var entries = await new AuditLogService(dbContext).GetRecentAsync(AuditLogService.DefaultLimit);

        Assert.Equal(new[] { "Logout", "UserDeactivated", "LoginSucceeded" }, entries.Select(e => e.Action).ToArray());
        Assert.Equal(entries.OrderByDescending(e => e.OccurredAt).Select(e => e.Id), entries.Select(e => e.Id));
    }

    [Fact]
    public async Task EachEntryCarriesUsernameActionTimestampAndIpAddress()
    {
        await using var dbContext = CreateDbContext();
        var admin = await SeedUserAsync(dbContext, "admin");
        dbContext.LoginAuditEntries.Add(Login(admin.Id, LoginOutcome.Success, minute: 5, ip: "203.0.113.7"));
        await dbContext.SaveChangesAsync();

        var entry = Assert.Single(await new AuditLogService(dbContext).GetRecentAsync(AuditLogService.DefaultLimit));

        Assert.Equal("admin", entry.Username);
        Assert.Equal("LoginSucceeded", entry.Action);
        Assert.Equal(BaseTime.AddMinutes(5), entry.OccurredAt);
        Assert.Equal(DateTimeKind.Utc, entry.OccurredAt.Kind);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Null(entry.TargetUsername);
    }

    [Theory]
    [InlineData(LoginOutcome.Success, "LoginSucceeded")]
    [InlineData(LoginOutcome.InvalidCredentials, "LoginFailed")]
    [InlineData(LoginOutcome.AccountDeactivated, "LoginBlocked")]
    public async Task EachLoginOutcomeHasItsOwnAction(LoginOutcome outcome, string expectedAction)
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, "dr.chen", UserRole.Doctor);
        dbContext.LoginAuditEntries.Add(Login(user.Id, outcome, minute: 1));
        await dbContext.SaveChangesAsync();

        var entry = Assert.Single(await new AuditLogService(dbContext).GetRecentAsync(AuditLogService.DefaultLimit));

        Assert.Equal(expectedAction, entry.Action);
    }

    [Theory]
    [InlineData(AdminAuditAction.UserCreated)]
    [InlineData(AdminAuditAction.UserUpdated)]
    [InlineData(AdminAuditAction.PasswordReset)]
    [InlineData(AdminAuditAction.UserDeactivated)]
    [InlineData(AdminAuditAction.UserReactivated)]
    public async Task AdminActionNamesTheActingAdminAndTheTargetAccount(AdminAuditAction action)
    {
        await using var dbContext = CreateDbContext();
        var admin = await SeedUserAsync(dbContext, "admin");
        var doctor = await SeedUserAsync(dbContext, "dr.chen", UserRole.Doctor);
        dbContext.AdminAuditEntries.Add(AdminAction(admin.Id, doctor.Id, action, minute: 1));
        await dbContext.SaveChangesAsync();

        var entry = Assert.Single(await new AuditLogService(dbContext).GetRecentAsync(AuditLogService.DefaultLimit));

        Assert.Equal(action.ToString(), entry.Action);
        Assert.Equal("admin", entry.Username);
        Assert.Equal("dr.chen", entry.TargetUsername);
    }

    [Fact]
    public async Task FailedLoginForAnUnknownUsernameIsListedAsUnknown()
    {
        await using var dbContext = CreateDbContext();
        dbContext.LoginAuditEntries.Add(Login(userId: null, LoginOutcome.InvalidCredentials, minute: 1));
        await dbContext.SaveChangesAsync();

        var entry = Assert.Single(await new AuditLogService(dbContext).GetRecentAsync(AuditLogService.DefaultLimit));

        Assert.Equal("Unknown", entry.Username);
        Assert.Equal("LoginFailed", entry.Action);
    }

    [Fact]
    public async Task EntriesOfASoftDeletedAccountKeepItsUsername()
    {
        await using var dbContext = CreateDbContext();
        var former = await SeedUserAsync(dbContext, "former.staff", UserRole.Receptionist, isDeleted: true);
        dbContext.LogoutAuditEntries.Add(Logout(former.Id, minute: 1));
        await dbContext.SaveChangesAsync();

        var entry = Assert.Single(await new AuditLogService(dbContext).GetRecentAsync(AuditLogService.DefaultLimit));

        Assert.Equal("former.staff", entry.Username);
    }

    [Fact]
    public async Task LimitKeepsTheNewestEntriesAcrossAllSources()
    {
        await using var dbContext = CreateDbContext();
        var admin = await SeedUserAsync(dbContext, "admin");
        for (var minute = 1; minute <= 4; minute++)
        {
            dbContext.LoginAuditEntries.Add(Login(admin.Id, LoginOutcome.Success, minute));
        }

        dbContext.LogoutAuditEntries.Add(Logout(admin.Id, minute: 10));
        dbContext.AdminAuditEntries.Add(AdminAction(admin.Id, admin.Id, AdminAuditAction.UserUpdated, minute: 9));
        await dbContext.SaveChangesAsync();

        var entries = await new AuditLogService(dbContext).GetRecentAsync(limit: 3);

        Assert.Equal(new[] { "Logout", "UserUpdated", "LoginSucceeded" }, entries.Select(e => e.Action).ToArray());
        Assert.Equal(BaseTime.AddMinutes(4), entries[2].OccurredAt);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100_000, AuditLogService.MaximumLimit)]
    public async Task LimitIsClampedToTheSupportedRange(int requested, int expectedCount)
    {
        await using var dbContext = CreateDbContext();
        var admin = await SeedUserAsync(dbContext, "admin");
        for (var minute = 0; minute < AuditLogService.MaximumLimit + 5; minute++)
        {
            dbContext.LogoutAuditEntries.Add(Logout(admin.Id, minute));
        }

        await dbContext.SaveChangesAsync();

        var entries = await new AuditLogService(dbContext).GetRecentAsync(requested);

        Assert.Equal(expectedCount, entries.Count);
    }

    [Fact]
    public async Task ReadingTheLogWritesNothing()
    {
        await using var dbContext = CreateDbContext();
        var admin = await SeedUserAsync(dbContext, "admin");
        dbContext.LogoutAuditEntries.Add(Logout(admin.Id, minute: 1));
        await dbContext.SaveChangesAsync();

        await new AuditLogService(dbContext).GetRecentAsync(AuditLogService.DefaultLimit);

        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Single(dbContext.LogoutAuditEntries);
        Assert.Empty(dbContext.LoginAuditEntries);
        Assert.Empty(dbContext.AdminAuditEntries);
    }

    // Drives the real services end to end, so every audited action in the acceptance
    // criteria is shown to reach the log without being seeded by hand.
    [Fact]
    public async Task LoginLogoutAndEveryAdminActionAreRecordedInTheLog()
    {
        await using var dbContext = CreateDbContext();
        var admin = await SeedUserAsync(dbContext, "admin");
        var context = new AdminActionContext(admin.Id, "correlation", "10.0.0.9");
        var tokens = new Mock<IJwtTokenService>();
        tokens.Setup(s => s.GenerateToken(It.IsAny<User>())).Returns(("signed-jwt", BaseTime.AddHours(12)));
        var authentication = new AuthenticationService(
            dbContext, tokens.Object, NullLogger<AuthenticationService>.Instance);
        var accounts = new UserAccountService(dbContext, NullLogger<UserAccountService>.Instance);

        await authentication.LoginAsync("admin", Password, "correlation", "10.0.0.9");
        var created = await accounts.CreateUserAsync(
            new CreateUserRequest
            {
                Username = "dr.new",
                Password = Password,
                FullName = "Dr. New Doctor",
                Role = UserRole.Doctor,
                RoomNumber = "R-301"
            },
            context);
        var doctorId = created.User!.UserId;
        await accounts.UpdateUserAsync(
            doctorId, new UpdateUserRequest { FullName = "Dr. Renamed", RoomNumber = "R-302" }, context);
        await accounts.ResetPasswordAsync(doctorId, "a-brand-new-password", context);
        await accounts.DeactivateUserAsync(doctorId, context);
        await accounts.ReactivateUserAsync(doctorId, context);
        await authentication.LogoutAsync(admin.Id, "correlation", "10.0.0.9");

        var entries = await new AuditLogService(dbContext).GetRecentAsync(AuditLogService.DefaultLimit);

        Assert.Equal(
            new[]
            {
                "LoginSucceeded", "Logout", "PasswordReset", "UserCreated",
                "UserDeactivated", "UserReactivated", "UserUpdated"
            },
            entries.Select(e => e.Action).Order().ToArray());
        Assert.All(entries, entry => Assert.Equal("admin", entry.Username));
        Assert.All(entries, entry => Assert.Equal("10.0.0.9", entry.IpAddress));
    }
}
