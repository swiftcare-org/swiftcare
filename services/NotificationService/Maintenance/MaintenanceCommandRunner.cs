using Microsoft.EntityFrameworkCore;
using NotificationService.Data;

namespace NotificationService.Maintenance;

public static class MaintenanceCommandRunner
{
    public const int Success = 0;
    public const int Failure = 1;

    public static async Task<int> RunAsync(
        MaintenanceCommand command,
        CancellationToken cancellationToken = default)
    {
        // A valueless flag such as --migrate is rejected by the command-line
        // configuration provider, so maintenance configuration intentionally uses
        // appsettings and environment variables only.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("NotificationDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await Console.Error.WriteLineAsync("ConnectionStrings__NotificationDb is not configured.");
            return Failure;
        }

        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseMySql(
                connectionString,
                new MySqlServerVersion(new Version(8, 4, 0)),
                mysql => mysql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null))
            .Options;

        await using var dbContext = new NotificationDbContext(options);

        try
        {
            return command switch
            {
                MaintenanceCommand.Migrate => await MigrateAsync(dbContext, cancellationToken),
                _ => Failure
            };
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"{command} failed: {exception.Message}");
            return Failure;
        }
    }

    public static async Task<int> MigrateAsync(
        NotificationDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            await Console.Out.WriteLineAsync("No pending migrations. Database is up to date.");
            return Success;
        }

        await Console.Out.WriteLineAsync(
            $"Applying {pending.Count} migration(s): {string.Join(", ", pending)}");
        await dbContext.Database.MigrateAsync(cancellationToken);
        await Console.Out.WriteLineAsync("Migrations applied.");
        return Success;
    }
}
