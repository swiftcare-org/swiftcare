using AuthService.Data;
using AuthService.Maintenance;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AuthService.UnitTests.Maintenance;

public class BootstrapAdminTests
{
    private const string Username = "admin.bootstrap";
    private const string ValidPassword = "bootstrap-password";

    private static AuthDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IConfiguration CreateConfiguration(string? username, string? password, string? fullName = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INITIAL_ADMIN_USERNAME"] = username,
                ["INITIAL_ADMIN_PASSWORD"] = password,
                ["INITIAL_ADMIN_FULL_NAME"] = fullName
            })
            .Build();

    [Fact]
    public async Task Fails_WhenUsernameMissing()
    {
        await using var dbContext = CreateDbContext();

        var result = await MaintenanceCommandRunner.BootstrapAdminAsync(
            dbContext, CreateConfiguration(null, ValidPassword));

        Assert.Equal(MaintenanceCommandRunner.Failure, result);
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task Fails_WhenPasswordMissing()
    {
        await using var dbContext = CreateDbContext();

        var result = await MaintenanceCommandRunner.BootstrapAdminAsync(
            dbContext, CreateConfiguration(Username, null));

        Assert.Equal(MaintenanceCommandRunner.Failure, result);
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task Fails_WhenPasswordBelowMinimumLength()
    {
        await using var dbContext = CreateDbContext();

        var result = await MaintenanceCommandRunner.BootstrapAdminAsync(
            dbContext, CreateConfiguration(Username, "short"));

        Assert.Equal(MaintenanceCommandRunner.Failure, result);
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task CreatesActiveAdmin_WhenNoneExists()
    {
        await using var dbContext = CreateDbContext();

        var result = await MaintenanceCommandRunner.BootstrapAdminAsync(
            dbContext, CreateConfiguration(Username, ValidPassword, "Ops Admin"));

        Assert.Equal(MaintenanceCommandRunner.Success, result);

        var created = Assert.Single(dbContext.Users);
        Assert.Equal(Username, created.Username);
        Assert.Equal(UserRole.Admin, created.Role);
        Assert.Equal("Ops Admin", created.FullName);
        Assert.True(created.IsActive);
        Assert.Null(created.RoomNumber);
    }

    [Fact]
    public async Task StoresPasswordAsVerifiableHash_NotPlaintext()
    {
        await using var dbContext = CreateDbContext();

        await MaintenanceCommandRunner.BootstrapAdminAsync(
            dbContext, CreateConfiguration(Username, ValidPassword));

        var created = Assert.Single(dbContext.Users);
        Assert.NotEqual(ValidPassword, created.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(ValidPassword, created.PasswordHash));
    }

    [Fact]
    public async Task IsIdempotent_WhenRunTwice()
    {
        await using var dbContext = CreateDbContext();
        var configuration = CreateConfiguration(Username, ValidPassword);

        var first = await MaintenanceCommandRunner.BootstrapAdminAsync(dbContext, configuration);
        var second = await MaintenanceCommandRunner.BootstrapAdminAsync(dbContext, configuration);

        Assert.Equal(MaintenanceCommandRunner.Success, first);
        Assert.Equal(MaintenanceCommandRunner.Success, second);
        Assert.Single(dbContext.Users);
    }

    [Fact]
    public async Task Fails_WhenUsernameTakenByNonAdmin()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(new User
        {
            Username = Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FullName = "Existing Receptionist",
            Role = UserRole.Receptionist
        });
        await dbContext.SaveChangesAsync();

        var result = await MaintenanceCommandRunner.BootstrapAdminAsync(
            dbContext, CreateConfiguration(Username, ValidPassword));

        Assert.Equal(MaintenanceCommandRunner.Failure, result);
        Assert.Single(dbContext.Users);
    }
}
