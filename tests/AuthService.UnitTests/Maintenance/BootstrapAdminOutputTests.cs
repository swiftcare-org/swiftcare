using AuthService.Data;
using AuthService.Maintenance;
using AuthService.Models.Configuration;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AuthService.UnitTests.Maintenance;

[CollectionDefinition(nameof(ConsoleOutputCollection), DisableParallelization = true)]
public sealed class ConsoleOutputCollection;

// The bootstrap command runs in the CD pipeline, where its console output is the only way
// an operator learns why a deployment step failed. Console redirection is process-wide,
// so these tests run outside the parallel test collections (SWC-151 mutation testing).
[Collection(nameof(ConsoleOutputCollection))]
public class BootstrapAdminOutputTests
{
    private const string Username = "admin.bootstrap";
    private const string ValidPassword = "bootstrap-password";

    [Theory]
    [InlineData(null, ValidPassword, "INITIAL_ADMIN_USERNAME is not configured.")]
    [InlineData(Username, null, "INITIAL_ADMIN_PASSWORD is not configured.")]
    [InlineData(Username, "short", "INITIAL_ADMIN_PASSWORD must be at least 8 characters.")]
    public async Task MissingOrWeakSettingsExplainWhatToFix(string? username, string? password, string message)
    {
        await using var dbContext = CreateDbContext();

        var (result, error, _) = await RunAsync(dbContext, Configuration(username, password));

        Assert.Equal(MaintenanceCommandRunner.Failure, result);
        Assert.Equal(message, error.Trim());
    }

    [Fact]
    public async Task PasswordOfExactlyTheMinimumLengthIsAccepted()
    {
        await using var dbContext = CreateDbContext();
        var password = new string('x', PasswordPolicy.MinimumLength);

        var (result, _, output) = await RunAsync(dbContext, Configuration(Username, password));

        Assert.Equal(MaintenanceCommandRunner.Success, result);
        Assert.Equal($"Administrator '{Username}' created.", output.Trim());
    }

    [Fact]
    public async Task AdministratorWithoutAConfiguredNameGetsTheDefaultName()
    {
        await using var dbContext = CreateDbContext();

        await RunAsync(dbContext, Configuration(Username, ValidPassword));

        Assert.Equal("SwiftCare Administrator", (await dbContext.Users.SingleAsync()).FullName);
    }

    [Fact]
    public async Task ExistingAdministratorIsReportedAndLeftUnchanged()
    {
        await using var dbContext = CreateDbContext();
        await RunAsync(dbContext, Configuration(Username, ValidPassword));

        var (result, _, output) = await RunAsync(dbContext, Configuration(Username, ValidPassword));

        Assert.Equal(MaintenanceCommandRunner.Success, result);
        Assert.Equal($"Administrator '{Username}' already exists. No change made.", output.Trim());
    }

    [Fact]
    public async Task UsernameTakenByANonAdministratorIsReported()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(new User
        {
            Username = Username,
            PasswordHash = "hash",
            FullName = "Nimali Perera",
            Role = UserRole.Receptionist
        });
        await dbContext.SaveChangesAsync();

        var (result, error, _) = await RunAsync(dbContext, Configuration(Username, ValidPassword));

        Assert.Equal(MaintenanceCommandRunner.Failure, result);
        Assert.Equal($"User '{Username}' already exists and is not an administrator.", error.Trim());
    }

    private static async Task<(int Result, string Error, string Output)> RunAsync(
        AuthDbContext dbContext,
        IConfiguration configuration)
    {
        var originalError = Console.Error;
        var originalOutput = Console.Out;
        using var error = new StringWriter();
        using var output = new StringWriter();
        Console.SetError(error);
        Console.SetOut(output);
        try
        {
            var result = await MaintenanceCommandRunner.BootstrapAdminAsync(dbContext, configuration);
            return (result, error.ToString(), output.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Console.SetOut(originalOutput);
        }
    }

    private static AuthDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IConfiguration Configuration(string? username, string? password) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INITIAL_ADMIN_USERNAME"] = username,
                ["INITIAL_ADMIN_PASSWORD"] = password
            })
            .Build();
}
