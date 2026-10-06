using AuthService.Data;
using AuthService.Models.Configuration;
using AuthService.Models.Dtos;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AuthService.UnitTests.Services;

// SWC-138: editing, password reset, deactivation and reactivation of existing accounts.
public class UserAccountManagementTests
{
    private const string OriginalPassword = "correct-horse-battery-staple";
    private static readonly AdminActionContext AdminContext = new(Guid.NewGuid(), "test-correlation-id", "127.0.0.1");

    private static AuthDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UserAccountService CreateService(AuthDbContext dbContext) =>
        new(dbContext, NullLogger<UserAccountService>.Instance);

    private static async Task<User> SeedUserAsync(
        AuthDbContext dbContext,
        UserRole role = UserRole.Doctor,
        bool isActive = true,
        bool isDeleted = false)
    {
        var user = new User
        {
            Username = "dr.chen",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(OriginalPassword),
            FullName = "Dr. Amara Chen",
            Role = role,
            RoomNumber = role == UserRole.Doctor ? "R-204" : null,
            IsActive = isActive,
            IsDeleted = isDeleted
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private static void AssertSingleAuditEntry(AuthDbContext dbContext, AdminAuditAction action, Guid targetUserId)
    {
        var entry = Assert.Single(dbContext.AdminAuditEntries);
        Assert.Equal(action, entry.Action);
        Assert.Equal(AdminContext.AdminUserId, entry.ActorUserId);
        Assert.Equal(targetUserId, entry.TargetUserId);
        Assert.Equal(AdminContext.IpAddress, entry.IpAddress);
    }

    [Fact]
    public async Task UpdateSavesNameRoomAndSpecializationForADoctor()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);

        var result = await CreateService(dbContext).UpdateUserAsync(
            user.Id,
            new UpdateUserRequest { FullName = " Dr. Amara Perera ", RoomNumber = " R-310 ", Specialization = " Cardiology " },
            AdminContext);

        Assert.Equal(UserActionOutcome.Success, result.Outcome);
        var persisted = Assert.Single(dbContext.Users);
        Assert.Equal("Dr. Amara Perera", persisted.FullName);
        Assert.Equal("R-310", persisted.RoomNumber);
        Assert.Equal("Cardiology", persisted.Specialization);
        Assert.Equal("Cardiology", result.User!.Specialization);
    }

    [Fact]
    public async Task UpdateLeavesTheUsernameRoleAndPasswordUnchanged()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);
        var originalHash = user.PasswordHash;

        await CreateService(dbContext).UpdateUserAsync(
            user.Id, new UpdateUserRequest { FullName = "Dr. Amara Perera", RoomNumber = "R-310" }, AdminContext);

        var persisted = Assert.Single(dbContext.Users);
        Assert.Equal("dr.chen", persisted.Username);
        Assert.Equal(UserRole.Doctor, persisted.Role);
        Assert.Equal(originalHash, persisted.PasswordHash);
    }

    [Theory]
    [InlineData("Username")]
    [InlineData("Role")]
    [InlineData("Password")]
    public void UpdateRequestCannotCarryAFieldThatMustNotChange(string propertyName)
    {
        Assert.Null(typeof(UpdateUserRequest).GetProperty(propertyName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateDoctorWithoutRoomNumberIsRejectedAndChangesNothing(string? roomNumber)
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);

        var result = await CreateService(dbContext).UpdateUserAsync(
            user.Id, new UpdateUserRequest { FullName = "Changed Name", RoomNumber = roomNumber }, AdminContext);

        Assert.Equal(UserActionOutcome.RoomNumberRequiredForDoctor, result.Outcome);
        var persisted = Assert.Single(dbContext.Users);
        Assert.Equal("Dr. Amara Chen", persisted.FullName);
        Assert.Equal("R-204", persisted.RoomNumber);
        Assert.Empty(dbContext.AdminAuditEntries);
    }

    [Theory]
    [InlineData(UserRole.Receptionist)]
    [InlineData(UserRole.Admin)]
    public async Task UpdateNonDoctorStoresNoRoomNumberOrSpecialization(UserRole role)
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, role);

        var result = await CreateService(dbContext).UpdateUserAsync(
            user.Id,
            new UpdateUserRequest { FullName = "Renamed Staff", RoomNumber = "R-999", Specialization = "Cardiology" },
            AdminContext);

        Assert.Equal(UserActionOutcome.Success, result.Outcome);
        var persisted = Assert.Single(dbContext.Users);
        Assert.Equal("Renamed Staff", persisted.FullName);
        Assert.Null(persisted.RoomNumber);
        Assert.Null(persisted.Specialization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateWithBlankSpecializationClearsIt(string? specialization)
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);
        user.Specialization = "Cardiology";
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).UpdateUserAsync(
            user.Id,
            new UpdateUserRequest { FullName = user.FullName, RoomNumber = "R-204", Specialization = specialization },
            AdminContext);

        Assert.Null(Assert.Single(dbContext.Users).Specialization);
    }

    [Fact]
    public async Task UpdateUnknownUserReturnsNotFound()
    {
        await using var dbContext = CreateDbContext();

        var result = await CreateService(dbContext).UpdateUserAsync(
            Guid.NewGuid(), new UpdateUserRequest { FullName = "Nobody" }, AdminContext);

        Assert.Equal(UserActionOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task UpdateSoftDeletedUserReturnsNotFound()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, isDeleted: true);

        var result = await CreateService(dbContext).UpdateUserAsync(
            user.Id, new UpdateUserRequest { FullName = "Changed Name", RoomNumber = "R-310" }, AdminContext);

        Assert.Equal(UserActionOutcome.NotFound, result.Outcome);
        Assert.Equal("Dr. Amara Chen", Assert.Single(dbContext.Users).FullName);
    }

    [Fact]
    public async Task UpdateRecordsOneAuditEntryForTheActingAdmin()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);

        await CreateService(dbContext).UpdateUserAsync(
            user.Id, new UpdateUserRequest { FullName = "Dr. Amara Perera", RoomNumber = "R-310" }, AdminContext);

        AssertSingleAuditEntry(dbContext, AdminAuditAction.UserUpdated, user.Id);
    }

    // Signs in through the real AuthenticationService, so these tests prove what the
    // acceptance criteria describe rather than only inspecting the stored row.
    private static async Task<LoginOutcome> LoginAsync(AuthDbContext dbContext, string username, string password)
    {
        var tokens = new Mock<IJwtTokenService>();
        tokens.Setup(s => s.GenerateToken(It.IsAny<User>())).Returns(("signed-jwt", DateTime.UtcNow.AddHours(12)));
        var authentication = new AuthenticationService(
            dbContext, tokens.Object, NullLogger<AuthenticationService>.Instance);

        var result = await authentication.LoginAsync(username, password, "login-correlation-id", "127.0.0.1");
        return result.Outcome;
    }

    [Fact]
    public async Task ResetPasswordStoresABcryptHashOfTheNewPassword()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);

        var result = await CreateService(dbContext).ResetPasswordAsync(user.Id, "a-brand-new-password", AdminContext);

        Assert.Equal(UserActionOutcome.Success, result.Outcome);
        var persisted = Assert.Single(dbContext.Users);
        Assert.NotEqual("a-brand-new-password", persisted.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("a-brand-new-password", persisted.PasswordHash));
    }

    [Fact]
    public async Task ResetPasswordLetsTheUserLogInWithTheNewPasswordAndNotTheOldOne()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);

        await CreateService(dbContext).ResetPasswordAsync(user.Id, "a-brand-new-password", AdminContext);

        Assert.Equal(LoginOutcome.Success, await LoginAsync(dbContext, user.Username, "a-brand-new-password"));
        Assert.Equal(LoginOutcome.InvalidCredentials, await LoginAsync(dbContext, user.Username, OriginalPassword));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1234567")]
    public async Task ResetPasswordShorterThanMinimumIsRejectedAndKeepsTheOldPassword(string shortPassword)
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);

        var result = await CreateService(dbContext).ResetPasswordAsync(user.Id, shortPassword, AdminContext);

        Assert.Equal(UserActionOutcome.PasswordTooShort, result.Outcome);
        Assert.True(BCrypt.Net.BCrypt.Verify(OriginalPassword, Assert.Single(dbContext.Users).PasswordHash));
        Assert.Empty(dbContext.AdminAuditEntries);
    }

    [Fact]
    public async Task ResetPasswordAtMinimumLengthSucceeds()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);

        var result = await CreateService(dbContext).ResetPasswordAsync(
            user.Id, new string('a', PasswordPolicy.MinimumLength), AdminContext);

        Assert.Equal(UserActionOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task ResetPasswordForUnknownUserReturnsNotFound()
    {
        await using var dbContext = CreateDbContext();

        var result = await CreateService(dbContext).ResetPasswordAsync(
            Guid.NewGuid(), "a-brand-new-password", AdminContext);

        Assert.Equal(UserActionOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task ResetPasswordRecordsOneAuditEntryThatNeverHoldsThePassword()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);

        await CreateService(dbContext).ResetPasswordAsync(user.Id, "a-brand-new-password", AdminContext);

        AssertSingleAuditEntry(dbContext, AdminAuditAction.PasswordReset, user.Id);
        Assert.Null(typeof(AdminAuditEntry).GetProperty("Password"));
    }

    [Fact]
    public async Task DeactivateSetsTheAccountInactiveAndBlocksLogin()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);

        var result = await CreateService(dbContext).DeactivateUserAsync(user.Id, AdminContext);

        Assert.Equal(UserActionOutcome.Success, result.Outcome);
        Assert.False(result.User!.IsActive);
        Assert.False(Assert.Single(dbContext.Users).IsActive);
        Assert.Equal(LoginOutcome.AccountDeactivated, await LoginAsync(dbContext, user.Username, OriginalPassword));
    }

    [Fact]
    public async Task AdminCannotDeactivateTheirOwnAccount()
    {
        await using var dbContext = CreateDbContext();
        var admin = await SeedUserAsync(dbContext, UserRole.Admin);
        var ownContext = AdminContext with { AdminUserId = admin.Id };

        var result = await CreateService(dbContext).DeactivateUserAsync(admin.Id, ownContext);

        Assert.Equal(UserActionOutcome.CannotDeactivateOwnAccount, result.Outcome);
        Assert.True(Assert.Single(dbContext.Users).IsActive);
        Assert.Empty(dbContext.AdminAuditEntries);
    }

    [Fact]
    public async Task ReactivateSetsTheAccountActiveAndAllowsLoginAgain()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, isActive: false);

        var result = await CreateService(dbContext).ReactivateUserAsync(user.Id, AdminContext);

        Assert.Equal(UserActionOutcome.Success, result.Outcome);
        Assert.True(result.User!.IsActive);
        Assert.Equal(LoginOutcome.Success, await LoginAsync(dbContext, user.Username, OriginalPassword));
    }

    [Fact]
    public async Task DeactivateThenReactivateRecordsOneAuditEntryForEachChange()
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext);
        var service = CreateService(dbContext);

        await service.DeactivateUserAsync(user.Id, AdminContext);
        await service.ReactivateUserAsync(user.Id, AdminContext);

        Assert.Equal(
            new[] { AdminAuditAction.UserDeactivated, AdminAuditAction.UserReactivated },
            dbContext.AdminAuditEntries.OrderBy(e => e.OccurredAt).Select(e => e.Action).ToArray());
        Assert.All(dbContext.AdminAuditEntries, entry => Assert.Equal(user.Id, entry.TargetUserId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepeatingAStatusChangeSucceedsWithoutASecondAuditEntry(bool deactivate)
    {
        await using var dbContext = CreateDbContext();
        var user = await SeedUserAsync(dbContext, isActive: deactivate);
        var service = CreateService(dbContext);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = deactivate
                ? await service.DeactivateUserAsync(user.Id, AdminContext)
                : await service.ReactivateUserAsync(user.Id, AdminContext);
            Assert.Equal(UserActionOutcome.Success, result.Outcome);
        }

        Assert.Single(dbContext.AdminAuditEntries);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StatusChangeForUnknownUserReturnsNotFound(bool deactivate)
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var result = deactivate
            ? await service.DeactivateUserAsync(Guid.NewGuid(), AdminContext)
            : await service.ReactivateUserAsync(Guid.NewGuid(), AdminContext);

        Assert.Equal(UserActionOutcome.NotFound, result.Outcome);
    }
}
