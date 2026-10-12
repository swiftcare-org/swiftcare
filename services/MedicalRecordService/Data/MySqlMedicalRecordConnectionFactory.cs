using MySqlConnector;

namespace MedicalRecordService.Data;

public sealed class MySqlMedicalRecordConnectionFactory : IMedicalRecordConnectionFactory
{
    private readonly string _connectionString;

    public MySqlMedicalRecordConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MedicalRecordDb")
            ?? throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:MedicalRecordDb' is not configured.");
    }

    public async Task<MySqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
