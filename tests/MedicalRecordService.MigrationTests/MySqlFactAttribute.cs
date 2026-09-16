namespace MedicalRecordService.MigrationTests;

public sealed class MySqlFactAttribute : FactAttribute
{
    public const string ConnectionEnvironmentVariable =
        "MEDICAL_RECORD_MIGRATION_TEST_CONNECTION";

    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"Set {ConnectionEnvironmentVariable} to run MySQL migration tests.";
        }
    }
}
