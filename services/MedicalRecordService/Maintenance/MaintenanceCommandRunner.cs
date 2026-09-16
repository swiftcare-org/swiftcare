using MedicalRecordService.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalRecordService.Maintenance;

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

        var connectionString = configuration.GetConnectionString("MedicalRecordDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("ConnectionStrings__MedicalRecordDb is not configured.");
            return Failure;
        }

        var options = new DbContextOptionsBuilder<MedicalRecordDbContext>()
            .UseMySql(
                connectionString,
                new MySqlServerVersion(new Version(8, 4, 0)),
                mysql => mysql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null))
            .Options;

        await using var dbContext = new MedicalRecordDbContext(options);

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
            // Exception messages from database providers can contain server or connection
            // details. Report only the exception type and a safe operator action.
            Console.Error.WriteLine(
                $"MedicalRecordService migration failed ({exception.GetType().Name}). " +
                "Check database connectivity, permissions, and legacy schema compatibility.");
            return Failure;
        }
    }

    public static async Task<int> MigrateAsync(
        MedicalRecordDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await LegacySchemaBaseliner.BaselineIfNeededAsync(dbContext, cancellationToken);

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
}
