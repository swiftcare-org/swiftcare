using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Events;
using MySqlConnector;

namespace MedicalRecordService.Data;

public sealed class AdoNetConsultationCompletionRepository : IConsultationCompletionRepository
{
    private readonly IMedicalRecordConnectionFactory _connectionFactory;

    public AdoNetConsultationCompletionRepository(IMedicalRecordConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ConsultationProgressResponse?> FindByQueueAsync(
        Guid queueId,
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT consultation.Id, consultation.Status,
                   EXISTS (SELECT 1 FROM VitalSigns AS vitalSigns
                           WHERE vitalSigns.ConsultationId = consultation.Id)
            FROM Consultations AS consultation
            WHERE consultation.QueueId = @QueueId AND consultation.DoctorId = @DoctorId
            LIMIT 1;
            """;
        command.Parameters.Add("@QueueId", MySqlDbType.VarChar, 36).Value = queueId.ToString();
        command.Parameters.Add("@DoctorId", MySqlDbType.VarChar, 36).Value = doctorId.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ConsultationProgressResponse(
            Guid.Parse(reader.GetValue(0).ToString()!),
            queueId,
            reader.GetString(1),
            Convert.ToInt32(reader.GetValue(2)) == 1);
    }

    public async Task<CompletionPreparationResult> PrepareAsync(
        Guid consultationId,
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid? queueId = null;
        Guid? patientId = null;
        string? status = null;
        Guid? storedEventId;

        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT QueueId, PatientId, Status, EventId
                FROM Consultations
                WHERE Id = @ConsultationId AND DoctorId = @DoctorId
                FOR UPDATE;
                """;
            lookup.Parameters.Add("@ConsultationId", MySqlDbType.VarChar, 36).Value =
                consultationId.ToString();
            lookup.Parameters.Add("@DoctorId", MySqlDbType.VarChar, 36).Value =
                doctorId.ToString();

            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                queueId = Guid.Parse(reader.GetValue(0).ToString()!);
                patientId = Guid.Parse(reader.GetValue(1).ToString()!);
                status = reader.GetString(2);
                storedEventId = reader.IsDBNull(3)
                    ? null
                    : Guid.Parse(reader.GetValue(3).ToString()!);
            }
            else
            {
                storedEventId = null;
            }
        }

        if (queueId is null || patientId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CompletionPreparationResult(CompletionPreparationOutcome.ConsultationNotFound);
        }

        if (status == Consultation.CompleteStatus)
        {
            if (storedEventId is null || storedEventId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Completed consultation has no stored event ID.");
            }

            await transaction.RollbackAsync(cancellationToken);
            return new CompletionPreparationResult(
                CompletionPreparationOutcome.Ready,
                new ConsultationCompletedEvent(
                    storedEventId.Value, consultationId, queueId.Value, patientId.Value, doctorId));
        }

        if (status != Consultation.InProgressStatus || storedEventId is not null)
        {
            throw new InvalidOperationException("Consultation completion state is inconsistent.");
        }

        await using (var vitalSigns = connection.CreateCommand())
        {
            vitalSigns.Transaction = transaction;
            vitalSigns.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM VitalSigns WHERE ConsultationId = @ConsultationId
                );
                """;
            vitalSigns.Parameters.Add("@ConsultationId", MySqlDbType.VarChar, 36).Value =
                consultationId.ToString();

            var hasVitalSigns = Convert.ToInt32(
                await vitalSigns.ExecuteScalarAsync(cancellationToken)) == 1;
            if (!hasVitalSigns)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CompletionPreparationResult(CompletionPreparationOutcome.VitalSignsMissing);
            }
        }

        var eventId = Guid.NewGuid();
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Consultations
                SET Status = @CompleteStatus, EventId = @EventId
                WHERE Id = @ConsultationId
                  AND DoctorId = @DoctorId
                  AND Status = @InProgressStatus
                  AND EventId IS NULL;
                """;
            update.Parameters.Add("@CompleteStatus", MySqlDbType.VarChar, 16).Value =
                Consultation.CompleteStatus;
            update.Parameters.Add("@InProgressStatus", MySqlDbType.VarChar, 16).Value =
                Consultation.InProgressStatus;
            update.Parameters.Add("@EventId", MySqlDbType.VarChar, 36).Value = eventId.ToString();
            update.Parameters.Add("@ConsultationId", MySqlDbType.VarChar, 36).Value =
                consultationId.ToString();
            update.Parameters.Add("@DoctorId", MySqlDbType.VarChar, 36).Value =
                doctorId.ToString();

            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Consultation completion write did not succeed.");
            }
        }

        // Publishing happens only after this commit succeeds. A later retry reads EventId.
        await transaction.CommitAsync(cancellationToken);
        return new CompletionPreparationResult(
            CompletionPreparationOutcome.Ready,
            new ConsultationCompletedEvent(eventId, consultationId, queueId.Value, patientId.Value, doctorId));
    }
}
