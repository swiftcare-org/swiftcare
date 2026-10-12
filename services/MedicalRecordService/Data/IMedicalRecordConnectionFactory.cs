using MySqlConnector;

namespace MedicalRecordService.Data;

public interface IMedicalRecordConnectionFactory
{
    Task<MySqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default);
}
