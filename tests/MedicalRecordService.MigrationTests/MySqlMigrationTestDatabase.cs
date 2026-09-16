using MySqlConnector;

namespace MedicalRecordService.MigrationTests;

internal sealed class MySqlMigrationTestDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;

    private MySqlMigrationTestDatabase(
        string databaseName,
        string serverConnectionString,
        string connectionString)
    {
        DatabaseName = databaseName;
        _serverConnectionString = serverConnectionString;
        ConnectionString = connectionString;
    }

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    public static async Task<MySqlMigrationTestDatabase> CreateAsync()
    {
        var configuredConnection = Environment.GetEnvironmentVariable(
            MySqlFactAttribute.ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"{MySqlFactAttribute.ConnectionEnvironmentVariable} is not configured.");

        var serverBuilder = new MySqlConnectionStringBuilder(configuredConnection)
        {
            Database = string.Empty
        };
        // Pomelo includes the database name in a MySQL user-level migration lock,
        // whose complete name must remain within MySQL's 64-character limit.
        var databaseName = $"swc107_{Guid.NewGuid():N}";

        await using (var connection = new MySqlConnection(serverBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await command.ExecuteNonQueryAsync();
        }

        var databaseBuilder = new MySqlConnectionStringBuilder(serverBuilder.ConnectionString)
        {
            Database = databaseName
        };

        return new MySqlMigrationTestDatabase(
            databaseName,
            serverBuilder.ConnectionString,
            databaseBuilder.ConnectionString);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new MySqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{DatabaseName}`;";
        await command.ExecuteNonQueryAsync();
    }
}
