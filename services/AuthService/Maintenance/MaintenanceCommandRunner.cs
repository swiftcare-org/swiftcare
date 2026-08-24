using AuthService.Data;
using AuthService.Models.Configuration;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Maintenance;

public static class MaintenanceCommandRunner
{
    public const int Success = 0;
    public const int Failure = 1;

    public static async Task<int> RunAsync(MaintenanceCommand command, CancellationToken cancellationToken = default)
    {
        // Deliberately does not read command-line arguments: .NET's command-line
        // configuration provider rejects a valueless flag such as --migrate.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("AuthDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("ConnectionStrings__AuthDb is not configured.");
            return Failure;
        }

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseMySql(
                connectionString,
                new MySqlServerVersion(new Version(8, 4, 0)),
                mySqlOptions => mySqlOptions.EnableRetryOnFailure())
            .Options;

        await using var dbContext = new AuthDbContext(options);

        try
        {
            return command switch
            {
                MaintenanceCommand.Migrate => await MigrateAsync(dbContext, cancellationToken),
                MaintenanceCommand.BootstrapAdmin => await BootstrapAdminAsync(dbContext, configuration, cancellationToken),
                _ => Failure
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{command} failed: {exception.Message}");
            return Failure;
        }
    }

    public static async Task<int> MigrateAsync(AuthDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            Console.WriteLine("No pending migrations. Database is up to date.");
            return Success;
        }

        Console.WriteLine($"Applying {pending.Count} migration(s): {string.Join(", ", pending)}");
        await dbContext.Database.MigrateAsync(cancellationToken);
        Console.WriteLine("Migrations applied.");
        return Success;
    }

    // Never logs the supplied password or its hash.
    public static async Task<int> BootstrapAdminAsync(
        AuthDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var username = configuration["INITIAL_ADMIN_USERNAME"];
        var password = configuration["INITIAL_ADMIN_PASSWORD"];
        var fullName = configuration["INITIAL_ADMIN_FULL_NAME"];

        if (string.IsNullOrWhiteSpace(username))
        {
            Console.Error.WriteLine("INITIAL_ADMIN_USERNAME is not configured.");
            return Failure;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("INITIAL_ADMIN_PASSWORD is not configured.");
            return Failure;
        }

        if (password.Length < PasswordPolicy.MinimumLength)
        {
            Console.Error.WriteLine($"INITIAL_ADMIN_PASSWORD must be at least {PasswordPolicy.MinimumLength} characters.");
            return Failure;
        }

        var existing = await dbContext.Users
            .FirstOrDefaultAsync(user => user.Username == username, cancellationToken);

        if (existing is not null)
        {
            if (existing.Role != UserRole.Admin)
            {
                Console.Error.WriteLine($"User '{username}' already exists and is not an administrator.");
                return Failure;
            }

            // Idempotent: re-running after a failed deployment must not error or duplicate.
            Console.WriteLine($"Administrator '{username}' already exists. No change made.");
            return Success;
        }

        dbContext.Users.Add(new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FullName = string.IsNullOrWhiteSpace(fullName) ? "SwiftCare Administrator" : fullName,
            Role = UserRole.Admin,
            RoomNumber = null,
            IsActive = true
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        Console.WriteLine($"Administrator '{username}' created.");
        return Success;
    }
}
