using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;
using MySqlConnector;

namespace MedicalRecordService.Data;

public sealed class AdoNetConsultationRepository : IConsultationRepository
{
    private const int DuplicateKeyErrorNumber = 1062;

    private readonly IMedicalRecordConnectionFactory _connectionFactory;

    public AdoNetConsultationRepository(IMedicalRecordConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ConsultationPersistenceResult> CreateAsync(
        ConsultationDraft consultation,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var templateName = await FindTemplateNameAsync(
            connection,
            transaction,
            consultation.TemplateId,
            cancellationToken);

        if (consultation.TemplateId.HasValue && templateName is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ConsultationPersistenceResult
            {
                Outcome = ConsultationPersistenceOutcome.TemplateNotFound
            };
        }

        try
        {
            await InsertConsultationAsync(
                connection,
                transaction,
                consultation,
                templateName,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ConsultationPersistenceResult
            {
                Outcome = ConsultationPersistenceOutcome.Success,
                TemplateName = templateName
            };
        }
        catch (MySqlException exception) when (exception.Number == DuplicateKeyErrorNumber)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ConsultationPersistenceResult
            {
                Outcome = ConsultationPersistenceOutcome.QueueAlreadyHasConsultation
            };
        }
    }

    private static async Task<string?> FindTemplateNameAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid? templateId,
        CancellationToken cancellationToken)
    {
        if (!templateId.HasValue)
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Name
            FROM ConsultationTemplates
            WHERE Id = @TemplateId AND IsActive = TRUE
            LIMIT 1;
            """;
        command.Parameters.Add("@TemplateId", MySqlDbType.VarChar, 36).Value =
            templateId.Value.ToString();

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task InsertConsultationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ConsultationDraft consultation,
        string? templateName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Consultations (
                Id,
                PatientId,
                QueueId,
                DoctorId,
                DoctorName,
                RoomNumber,
                Symptoms,
                ExaminationFindings,
                Diagnosis,
                Notes,
                TemplateId,
                TemplateName,
                ConsultationDate,
                CreatedAt
            )
            VALUES (
                @Id,
                @PatientId,
                @QueueId,
                @DoctorId,
                @DoctorName,
                @RoomNumber,
                @Symptoms,
                @ExaminationFindings,
                @Diagnosis,
                @Notes,
                @TemplateId,
                @TemplateName,
                @ConsultationDate,
                @CreatedAt
            );
            """;

        command.Parameters.Add("@Id", MySqlDbType.VarChar, 36).Value = consultation.Id.ToString();
        command.Parameters.Add("@PatientId", MySqlDbType.VarChar, 36).Value =
            consultation.PatientId.ToString();
        command.Parameters.Add("@QueueId", MySqlDbType.VarChar, 36).Value =
            consultation.QueueId.ToString();
        command.Parameters.Add("@DoctorId", MySqlDbType.VarChar, 36).Value =
            consultation.DoctorId.ToString();
        command.Parameters.Add("@DoctorName", MySqlDbType.VarChar, 200).Value = consultation.DoctorName;
        command.Parameters.Add("@RoomNumber", MySqlDbType.VarChar, 50).Value = consultation.RoomNumber;
        command.Parameters.Add("@Symptoms", MySqlDbType.Text).Value = consultation.Symptoms;
        command.Parameters.Add("@ExaminationFindings", MySqlDbType.Text).Value =
            (object?)consultation.ExaminationFindings ?? DBNull.Value;
        command.Parameters.Add("@Diagnosis", MySqlDbType.Text).Value = consultation.Diagnosis;
        command.Parameters.Add("@Notes", MySqlDbType.Text).Value =
            (object?)consultation.Notes ?? DBNull.Value;
        command.Parameters.Add("@TemplateId", MySqlDbType.VarChar, 36).Value =
            consultation.TemplateId.HasValue
                ? consultation.TemplateId.Value.ToString()
                : DBNull.Value;
        command.Parameters.Add("@TemplateName", MySqlDbType.VarChar, 150).Value =
            (object?)templateName ?? DBNull.Value;
        command.Parameters.Add("@ConsultationDate", MySqlDbType.DateTime).Value =
            consultation.ConsultationDate;
        command.Parameters.Add("@CreatedAt", MySqlDbType.DateTime).Value =
            consultation.ConsultationDate;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
