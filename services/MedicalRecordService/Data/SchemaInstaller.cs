using MySqlConnector;

namespace MedicalRecordService.Data;

public static class SchemaInstaller
{
    public static async Task<int> RunAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("MedicalRecordDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("ConnectionStrings__MedicalRecordDb is required to apply the schema.");
            return 1;
        }

        try
        {
            using var stream = typeof(SchemaInstaller).Assembly.GetManifestResourceStream(
                "MedicalRecordService.Database.schema.sql")
                ?? throw new InvalidOperationException("Embedded schema is missing.");
            using var reader = new StreamReader(stream);
            var schema = await reader.ReadToEndAsync();
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = schema;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync();
            Console.WriteLine("MedicalRecordService schema applied successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            // Do not expose database credentials or patient data in job output.
            Console.Error.WriteLine($"MedicalRecordService schema failed ({exception.GetType().Name}). Check database connectivity and schema permissions.");
            return 1;
        }
    }
}
