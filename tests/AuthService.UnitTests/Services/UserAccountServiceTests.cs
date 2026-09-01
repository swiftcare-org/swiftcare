using AuthService.Data;
using AuthService.Models.Configuration;
using AuthService.Models.Dtos;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthService.UnitTests.Services;

public class UserAccountServiceTests
{
    private const string ValidPassword = "correct-horse-battery-staple";
    private const string CorrelationId = "test-correlation-id";
    private static readonly Guid ActingAdminId = Guid.NewGuid();

    private static AuthDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UserAccountService CreateService(AuthDbContext dbContext) =>
        new(dbContext, NullLogger<UserAccountService>.Instance);

    private static CreateUserRequest CreateValidRequest(
        string username = "dr.new",
        string password = ValidPassword,
        string fullName = "Dr. New Doctor",
        UserRole? role = UserRole.Doctor,
        string? roomNumber = "R-301") => new()
        {
            Username = username,
            Password = password,
            FullName = fullName,
            Role = role,
            RoomNumber = roomNumber
        };

    [Fact]
    public async Task CreateDoctorWithValidRequestPersistsUserWithBcryptHashedPassword()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var result = await service.CreateUserAsync(CreateValidRequest(), CorrelationId, ActingAdminId);

        Assert.Equal(CreateUserOutcome.Success, result.Outcome);
        var persisted = Assert.Single(dbContext.Users);
        Assert.NotEqual(ValidPassword, persisted.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(ValidPassword, persisted.PasswordHash));
        Assert.Equal("R-301", persisted.RoomNumber);
        Assert.True(persisted.IsActive);
    }

    [Fact]
    public async Task CreateReceptionistWithValidRequestPersistsUserWithNullRoomNumber()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var request = CreateValidRequest(username: "reception.new", role: UserRole.Receptionist, roomNumber: null);
        var result = await service.CreateUserAsync(request, CorrelationId, ActingAdminId);

        Assert.Equal(CreateUserOutcome.Success, result.Outcome);
        var persisted = Assert.Single(dbContext.Users);
        Assert.Null(persisted.RoomNumber);
    }

    [Fact]
    public async Task CreateWithExistingUsernameReturnsDuplicateUsername()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(new User
        {
            Username = "dr.new",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FullName = "Existing Doctor",
            Role = UserRole.Doctor,
            RoomNumber = "R-100"
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var result = await service.CreateUserAsync(CreateValidRequest(), CorrelationId, ActingAdminId);

        Assert.Equal(CreateUserOutcome.DuplicateUsername, result.Outcome);
    }

    [Fact]
    public async Task CreateWithExistingSoftDeletedUsernameReturnsDuplicateUsername()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(new User
        {
            Username = "dr.new",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FullName = "Former Doctor",
            Role = UserRole.Doctor,
            RoomNumber = "R-100",
            IsDeleted = true
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var result = await service.CreateUserAsync(CreateValidRequest(), CorrelationId, ActingAdminId);

        Assert.Equal(CreateUserOutcome.DuplicateUsername, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1234567")]
    public async Task CreateWithPasswordShorterThanMinimumReturnsPasswordTooShort(string shortPassword)
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var result = await service.CreateUserAsync(
            CreateValidRequest(password: shortPassword), CorrelationId, ActingAdminId);

        Assert.Equal(CreateUserOutcome.PasswordTooShort, result.Outcome);
    }

    [Fact]
    public async Task CreateWithPasswordAtMinimumLengthSucceeds()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var minimumLengthPassword = new string('a', PasswordPolicy.MinimumLength);
        var result = await service.CreateUserAsync(
            CreateValidRequest(password: minimumLengthPassword), CorrelationId, ActingAdminId);

        Assert.Equal(CreateUserOutcome.Success, result.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateDoctorWithoutRoomNumberReturnsRoomNumberRequiredForDoctor(string? roomNumber)
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var result = await service.CreateUserAsync(
            CreateValidRequest(roomNumber: roomNumber), CorrelationId, ActingAdminId);

        Assert.Equal(CreateUserOutcome.RoomNumberRequiredForDoctor, result.Outcome);
    }

    [Fact]
    public async Task CreateWithRoomNumberForNonDoctorStoresNullRoomNumber()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var request = CreateValidRequest(username: "admin.new", role: UserRole.Admin, roomNumber: "R-999");
        var result = await service.CreateUserAsync(request, CorrelationId, ActingAdminId);

        Assert.Equal(CreateUserOutcome.Success, result.Outcome);
        var persisted = Assert.Single(dbContext.Users);
        Assert.Null(persisted.RoomNumber);
    }

    [Fact]
    public async Task CreateFailureDoesNotPersistAnyUser()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await service.CreateUserAsync(CreateValidRequest(password: "short"), CorrelationId, ActingAdminId);
        await service.CreateUserAsync(CreateValidRequest(roomNumber: null), CorrelationId, ActingAdminId);

        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task GetUsersExcludesSoftDeletedUsers()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(new User
        {
            Username = "active.user",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FullName = "Active User",
            Role = UserRole.Receptionist
        });
        dbContext.Users.Add(new User
        {
            Username = "deleted.user",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FullName = "Deleted User",
            Role = UserRole.Receptionist,
            IsDeleted = true
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var users = await service.GetUsersAsync();

        var summary = Assert.Single(users);
        Assert.Equal("active.user", summary.Username);
    }

    [Fact]
    public async Task GetUsersReturnsNoPasswordHashProperty()
    {
        var passwordHashProperty = typeof(UserSummaryResponse).GetProperty("PasswordHash");

        Assert.Null(passwordHashProperty);
    }
}
