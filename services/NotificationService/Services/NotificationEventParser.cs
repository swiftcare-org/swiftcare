using System.Text.Json;
using NotificationService.Models.Configuration;
using NotificationService.Models.Entities;
using NotificationService.Models.Enums;
using NotificationService.Models.Events;

namespace NotificationService.Services;

// Turns one Kafka message into the notification to store. It has no side effects, so the
// rules for what counts as a valid event can be tested without Kafka or a database.
public sealed class NotificationEventParser
{
    // Matches the column sizes, so an oversized value is rejected here instead of failing
    // the insert and being retried forever.
    public const int QueueNumberMaxLength = 16;
    public const int DoctorNameMaxLength = 200;
    public const int RoomNumberMaxLength = 50;

    // A diagnosis is free text of any length at its source. The reports only group and
    // count it, so a longer one is shortened to fit the column instead of being refused.
    public const int DiagnosisMaxLength = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly KafkaOptions _options;

    public NotificationEventParser(KafkaOptions options)
    {
        _options = options;
    }

    // Returns null when the message is not valid JSON, is from an unknown topic, or lacks
    // an identifier the notification needs.
    public Notification? Parse(string topic, string payload, DateTime receivedAtUtc)
    {
        try
        {
            if (topic == _options.PatientCheckedInTopic)
            {
                return FromCheckedIn(Deserialize<PatientCheckedInEvent>(payload), receivedAtUtc);
            }

            if (topic == _options.PatientCalledTopic)
            {
                return FromCalled(Deserialize<PatientCalledEvent>(payload), receivedAtUtc);
            }

            if (topic == _options.ConsultationCompletedTopic)
            {
                return FromCompleted(Deserialize<ConsultationCompletedEvent>(payload), receivedAtUtc);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TEvent? Deserialize<TEvent>(string payload) =>
        JsonSerializer.Deserialize<TEvent>(payload, SerializerOptions);

    private static Notification? FromCheckedIn(PatientCheckedInEvent? checkedIn, DateTime receivedAtUtc)
    {
        if (checkedIn is null || AnyEmpty(checkedIn.EventId, checkedIn.PatientId) || checkedIn.CheckedInAt == default)
        {
            return null;
        }

        return new Notification
        {
            Id = Guid.NewGuid(),
            EventId = checkedIn.EventId,
            Type = NotificationType.PatientCheckedIn,
            PatientId = checkedIn.PatientId,
            IsNewPatient = checkedIn.IsNewPatient,
            OccurredAt = AsUtc(checkedIn.CheckedInAt),
            ReceivedAt = receivedAtUtc
        };
    }

    private static Notification? FromCalled(PatientCalledEvent? called, DateTime receivedAtUtc)
    {
        if (called is null
            || AnyEmpty(called.EventId, called.PatientId, called.QueueId, called.DoctorId)
            || called.CalledAt == default
            || !IsUsable(called.QueueNumber, QueueNumberMaxLength)
            || !IsUsable(called.DoctorName, DoctorNameMaxLength)
            || !IsUsable(called.RoomNumber, RoomNumberMaxLength))
        {
            return null;
        }

        return new Notification
        {
            Id = Guid.NewGuid(),
            EventId = called.EventId,
            Type = NotificationType.PatientCalled,
            PatientId = called.PatientId,
            QueueId = called.QueueId,
            DoctorId = called.DoctorId,
            QueueNumber = called.QueueNumber!.Trim(),
            DoctorName = called.DoctorName!.Trim(),
            RoomNumber = called.RoomNumber!.Trim(),
            OccurredAt = AsUtc(called.CalledAt),
            ReceivedAt = receivedAtUtc
        };
    }

    private static Notification? FromCompleted(ConsultationCompletedEvent? completed, DateTime receivedAtUtc)
    {
        if (completed is null
            || AnyEmpty(
                completed.EventId,
                completed.PatientId,
                completed.QueueId,
                completed.DoctorId,
                completed.ConsultationId))
        {
            return null;
        }

        return new Notification
        {
            Id = Guid.NewGuid(),
            EventId = completed.EventId,
            Type = NotificationType.ConsultationCompleted,
            PatientId = completed.PatientId,
            QueueId = completed.QueueId,
            DoctorId = completed.DoctorId,
            ConsultationId = completed.ConsultationId,
            Diagnosis = ShortenedDiagnosis(completed.Diagnosis),
            // The event has no timestamp, so the time it arrived is the best record there is.
            OccurredAt = receivedAtUtc,
            ReceivedAt = receivedAtUtc
        };
    }

    private static string? ShortenedDiagnosis(string? diagnosis)
    {
        var trimmed = diagnosis?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= DiagnosisMaxLength ? trimmed : trimmed[..DiagnosisMaxLength].TrimEnd();
    }

    private static bool AnyEmpty(params Guid[] ids) => ids.Any(id => id == Guid.Empty);

    private static bool IsUsable(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength;

    // Publishers write UTC timestamps. Normalizing here keeps every stored time comparable.
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
