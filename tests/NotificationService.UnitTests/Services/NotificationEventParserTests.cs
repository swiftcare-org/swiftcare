using System.Text.Json;
using NotificationService.Models.Configuration;
using NotificationService.Models.Enums;
using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

// SWC-143: each event type becomes the right notification, and a malformed event is refused.
public class NotificationEventParserTests
{
    private static readonly DateTime ReceivedAt = new(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EventTime = new(2026, 10, 8, 4, 30, 0, DateTimeKind.Utc);

    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly Guid QueueId = Guid.NewGuid();
    private static readonly Guid DoctorId = Guid.NewGuid();
    private static readonly Guid ConsultationId = Guid.NewGuid();

    private static readonly NotificationEventParser Parser =
        new(new KafkaOptions { BootstrapServers = "localhost:9092" });

    private static string CheckedIn(
        Guid? eventId = null, Guid? patientId = null, bool isNewPatient = true, DateTime? checkedInAt = null) =>
        JsonSerializer.Serialize(new
        {
            EventId = eventId ?? EventId,
            PatientId = patientId ?? PatientId,
            IsNewPatient = isNewPatient,
            CheckedInAt = checkedInAt ?? EventTime,
            CorrelationId = "correlation"
        });

    private static string Called(
        Guid? eventId = null,
        Guid? queueId = null,
        Guid? patientId = null,
        Guid? doctorId = null,
        string? queueNumber = "Q-007",
        string? doctorName = "Dr. Silva",
        string? roomNumber = "1",
        DateTime? calledAt = null) =>
        JsonSerializer.Serialize(new
        {
            EventId = eventId ?? EventId,
            QueueId = queueId ?? QueueId,
            PatientId = patientId ?? PatientId,
            QueueNumber = queueNumber,
            DoctorId = doctorId ?? DoctorId,
            DoctorName = doctorName,
            RoomNumber = roomNumber,
            CalledAt = calledAt ?? EventTime,
            CorrelationId = "correlation"
        });

    private static string Completed(
        Guid? eventId = null,
        Guid? consultationId = null,
        Guid? queueId = null,
        Guid? patientId = null,
        Guid? doctorId = null,
        string? diagnosis = "Viral URTI") =>
        JsonSerializer.Serialize(new
        {
            EventId = eventId ?? EventId,
            ConsultationId = consultationId ?? ConsultationId,
            QueueId = queueId ?? QueueId,
            PatientId = patientId ?? PatientId,
            DoctorId = doctorId ?? DoctorId,
            Diagnosis = diagnosis
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckInBecomesANotificationThatSaysWhetherThePatientIsNew(bool isNewPatient)
    {
        var notification = Parser.Parse("patient-checked-in", CheckedIn(isNewPatient: isNewPatient), ReceivedAt);

        Assert.NotNull(notification);
        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal(EventId, notification.EventId);
        Assert.Equal(NotificationType.PatientCheckedIn, notification.Type);
        Assert.Equal(PatientId, notification.PatientId);
        Assert.Equal(isNewPatient, notification.IsNewPatient);
        Assert.Equal(EventTime, notification.OccurredAt);
        Assert.Equal(ReceivedAt, notification.ReceivedAt);
        Assert.Null(notification.QueueNumber);
        Assert.Null(notification.RoomNumber);
        Assert.Null(notification.DoctorName);
    }

    [Fact]
    public void PatientCalledBecomesANotificationWithQueueNumberRoomAndDoctor()
    {
        var notification = Parser.Parse(
            "patient-called",
            Called(queueNumber: " Q-007 ", doctorName: " Dr. Silva ", roomNumber: " 1 "),
            ReceivedAt);

        Assert.NotNull(notification);
        Assert.Equal(EventId, notification.EventId);
        Assert.Equal(NotificationType.PatientCalled, notification.Type);
        Assert.Equal(PatientId, notification.PatientId);
        Assert.Equal(QueueId, notification.QueueId);
        Assert.Equal(DoctorId, notification.DoctorId);
        Assert.Equal("Q-007", notification.QueueNumber);
        Assert.Equal("Dr. Silva", notification.DoctorName);
        Assert.Equal("1", notification.RoomNumber);
        Assert.Equal(EventTime, notification.OccurredAt);
        Assert.Null(notification.IsNewPatient);
    }

    [Fact]
    public void ConsultationCompletedBecomesANotificationTimedByItsArrival()
    {
        var notification = Parser.Parse("consultation-completed", Completed(), ReceivedAt);

        Assert.NotNull(notification);
        Assert.Equal(EventId, notification.EventId);
        Assert.Equal(NotificationType.ConsultationCompleted, notification.Type);
        Assert.Equal(PatientId, notification.PatientId);
        Assert.Equal(QueueId, notification.QueueId);
        Assert.Equal(DoctorId, notification.DoctorId);
        Assert.Equal(ConsultationId, notification.ConsultationId);
        Assert.Equal("Viral URTI", notification.Diagnosis);
        // The event carries no timestamp of its own.
        Assert.Equal(ReceivedAt, notification.OccurredAt);
        Assert.Equal(ReceivedAt, notification.ReceivedAt);
    }

    [Fact]
    public void DiagnosisIsStoredWithoutSurroundingSpaces()
    {
        var notification = Parser.Parse("consultation-completed", Completed(diagnosis: "  Viral URTI  "), ReceivedAt);

        Assert.Equal("Viral URTI", notification!.Diagnosis);
    }

    // Events published before the diagnosis was added carry none, and are still stored.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConsultationCompletedWithoutADiagnosisIsStoredWithNone(string? diagnosis)
    {
        var notification = Parser.Parse("consultation-completed", Completed(diagnosis: diagnosis), ReceivedAt);

        Assert.NotNull(notification);
        Assert.Null(notification.Diagnosis);
    }

    [Fact]
    public void ConsultationCompletedFromBeforeTheDiagnosisWasAddedIsStillStored()
    {
        var payload = $$"""
            {"EventId":"{{EventId}}","ConsultationId":"{{ConsultationId}}","QueueId":"{{QueueId}}","PatientId":"{{PatientId}}","DoctorId":"{{DoctorId}}"}
            """;

        var notification = Parser.Parse("consultation-completed", payload, ReceivedAt);

        Assert.NotNull(notification);
        Assert.Null(notification.Diagnosis);
    }

    // A diagnosis is free text at its source. One longer than the column is shortened, so
    // the event is stored instead of failing the insert on every retry.
    [Fact]
    public void DiagnosisAtItsLimitIsKeptAndALongerOneIsShortened()
    {
        var atLimit = new string('D', NotificationEventParser.DiagnosisMaxLength);

        var kept = Parser.Parse("consultation-completed", Completed(diagnosis: atLimit), ReceivedAt);
        var shortened = Parser.Parse("consultation-completed", Completed(diagnosis: atLimit + "X"), ReceivedAt);

        Assert.Equal(atLimit, kept!.Diagnosis);
        Assert.Equal(atLimit, shortened!.Diagnosis);
    }

    [Fact]
    public void ShortenedDiagnosisDoesNotEndWithASpace()
    {
        var text = new string('D', NotificationEventParser.DiagnosisMaxLength - 1) + " tail";

        var notification = Parser.Parse("consultation-completed", Completed(diagnosis: text), ReceivedAt);

        Assert.Equal(new string('D', NotificationEventParser.DiagnosisMaxLength - 1), notification!.Diagnosis);
    }

    [Fact]
    public void DiagnosisLimitMatchesTheColumnSize()
    {
        Assert.Equal(200, NotificationEventParser.DiagnosisMaxLength);
    }

    [Fact]
    public void OtherEventTypesCarryNoDiagnosis()
    {
        Assert.Null(Parser.Parse("patient-checked-in", CheckedIn(), ReceivedAt)!.Diagnosis);
        Assert.Null(Parser.Parse("patient-called", Called(), ReceivedAt)!.Diagnosis);
    }

    [Fact]
    public void PropertyNamesAreMatchedWithoutRegardToCase()
    {
        var payload = $$"""
            {"eventId":"{{EventId}}","patientId":"{{PatientId}}","isNewPatient":false,"checkedInAt":"2026-10-08T04:30:00Z"}
            """;

        var notification = Parser.Parse("patient-checked-in", payload, ReceivedAt);

        Assert.NotNull(notification);
        Assert.Equal(EventId, notification.EventId);
        Assert.False(notification.IsNewPatient);
    }

    [Fact]
    public void EventTimeIsStoredAsUtc()
    {
        var unspecified = new DateTime(2026, 10, 8, 4, 30, 0, DateTimeKind.Unspecified);

        var notification = Parser.Parse("patient-checked-in", CheckedIn(checkedInAt: unspecified), ReceivedAt);

        Assert.Equal(DateTimeKind.Utc, notification!.OccurredAt.Kind);
        Assert.Equal(EventTime, notification.OccurredAt);
    }

    [Fact]
    public void LocalEventTimeIsConvertedToUtc()
    {
        var local = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Local);
        var payload = $$"""
            {"EventId":"{{EventId}}","PatientId":"{{PatientId}}","IsNewPatient":true,"CheckedInAt":"{{local:O}}"}
            """;

        var notification = Parser.Parse("patient-checked-in", payload, ReceivedAt);

        Assert.Equal(DateTimeKind.Utc, notification!.OccurredAt.Kind);
        Assert.Equal(local.ToUniversalTime(), notification.OccurredAt);
    }

    [Theory]
    [InlineData("{invalid json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void PayloadThatIsNotAnEventObjectIsRefused(string payload)
    {
        Assert.Null(Parser.Parse("patient-checked-in", payload, ReceivedAt));
        Assert.Null(Parser.Parse("patient-called", payload, ReceivedAt));
        Assert.Null(Parser.Parse("consultation-completed", payload, ReceivedAt));
    }

    [Fact]
    public void EmptyObjectIsRefusedForEveryTopic()
    {
        Assert.Null(Parser.Parse("patient-checked-in", "{}", ReceivedAt));
        Assert.Null(Parser.Parse("patient-called", "{}", ReceivedAt));
        Assert.Null(Parser.Parse("consultation-completed", "{}", ReceivedAt));
    }

    [Fact]
    public void EventFromAnUnknownTopicIsRefused()
    {
        Assert.Null(Parser.Parse("some-other-topic", CheckedIn(), ReceivedAt));
    }

    [Fact]
    public void CheckInWithoutAnEventIdPatientIdOrTimeIsRefused()
    {
        Assert.Null(Parser.Parse("patient-checked-in", CheckedIn(eventId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("patient-checked-in", CheckedIn(patientId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("patient-checked-in", CheckedIn(checkedInAt: default(DateTime)), ReceivedAt));
    }

    [Fact]
    public void PatientCalledWithoutAnIdentifierOrTimeIsRefused()
    {
        Assert.Null(Parser.Parse("patient-called", Called(eventId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("patient-called", Called(patientId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("patient-called", Called(queueId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("patient-called", Called(doctorId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("patient-called", Called(calledAt: default(DateTime)), ReceivedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PatientCalledWithoutQueueNumberDoctorNameOrRoomIsRefused(string? missing)
    {
        Assert.Null(Parser.Parse("patient-called", Called(queueNumber: missing), ReceivedAt));
        Assert.Null(Parser.Parse("patient-called", Called(doctorName: missing), ReceivedAt));
        Assert.Null(Parser.Parse("patient-called", Called(roomNumber: missing), ReceivedAt));
    }

    // A value longer than its column would fail the insert on every retry, so it is refused up front.
    [Fact]
    public void PatientCalledTextAtItsLimitIsAcceptedAndOneCharacterOverIsRefused()
    {
        var atLimit = Parser.Parse(
            "patient-called",
            Called(
                queueNumber: new string('Q', NotificationEventParser.QueueNumberMaxLength),
                doctorName: new string('D', NotificationEventParser.DoctorNameMaxLength),
                roomNumber: new string('R', NotificationEventParser.RoomNumberMaxLength)),
            ReceivedAt);

        Assert.NotNull(atLimit);
        Assert.Null(Parser.Parse(
            "patient-called",
            Called(queueNumber: new string('Q', NotificationEventParser.QueueNumberMaxLength + 1)),
            ReceivedAt));
        Assert.Null(Parser.Parse(
            "patient-called",
            Called(doctorName: new string('D', NotificationEventParser.DoctorNameMaxLength + 1)),
            ReceivedAt));
        Assert.Null(Parser.Parse(
            "patient-called",
            Called(roomNumber: new string('R', NotificationEventParser.RoomNumberMaxLength + 1)),
            ReceivedAt));
    }

    [Fact]
    public void TextLimitsMatchTheColumnSizes()
    {
        Assert.Equal(16, NotificationEventParser.QueueNumberMaxLength);
        Assert.Equal(200, NotificationEventParser.DoctorNameMaxLength);
        Assert.Equal(50, NotificationEventParser.RoomNumberMaxLength);
    }

    [Fact]
    public void ConsultationCompletedWithoutAnIdentifierIsRefused()
    {
        Assert.Null(Parser.Parse("consultation-completed", Completed(eventId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("consultation-completed", Completed(consultationId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("consultation-completed", Completed(queueId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("consultation-completed", Completed(patientId: Guid.Empty), ReceivedAt));
        Assert.Null(Parser.Parse("consultation-completed", Completed(doctorId: Guid.Empty), ReceivedAt));
    }

    [Fact]
    public void TopicNamesComeFromConfiguration()
    {
        var parser = new NotificationEventParser(new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            PatientCheckedInTopic = "custom-check-in"
        });

        Assert.NotNull(parser.Parse("custom-check-in", CheckedIn(), ReceivedAt));
        Assert.Null(parser.Parse("patient-checked-in", CheckedIn(), ReceivedAt));
    }
}
