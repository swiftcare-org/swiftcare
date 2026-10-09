using Confluent.Kafka;
using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Models.Events;
using MedicalRecordService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace MedicalRecordService.UnitTests.Services;

// Issue #121: a swallowed failure must say what kind of failure it was, without ever
// writing the exception message, which could carry patient data, to the log.
public class CompletionFailureLoggingTests
{
    private const string SensitiveMessage = "Duplicate entry for patient Nimal Perera, NIC 199012345678";

    [Fact]
    public async Task PublishFailureAfterCommitLogsTheExceptionTypeButNotItsMessage()
    {
        var consultationId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var completedEvent = NewEvent(consultationId, doctorId);
        var repository = new Mock<IConsultationCompletionRepository>();
        repository.Setup(item => item.PrepareAsync(consultationId, doctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompletionPreparationResult(CompletionPreparationOutcome.Ready, completedEvent));
        var publisher = new Mock<IConsultationCompletedPublisher>();
        publisher.Setup(item => item.PublishAsync(completedEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(SensitiveMessage));
        var logger = new RecordingLogger<ConsultationCompletionService>();
        var service = new ConsultationCompletionService(repository.Object, publisher.Object, logger);

        var result = await service.CompleteAsync(consultationId, doctorId);

        Assert.Equal(CompleteConsultationOutcome.PublishFailed, result.Outcome);
        var entry = Assert.Single(logger.Errors);
        Assert.Contains("Consultation completion publish failed", entry.Message);
        Assert.Contains($"consultationId={consultationId}", entry.Message);
        Assert.Contains($"eventId={completedEvent.EventId}", entry.Message);
        Assert.Contains("errorType=InvalidOperationException", entry.Message);
        Assert.DoesNotContain(SensitiveMessage, entry.Message);
        Assert.DoesNotContain("Nimal Perera", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task BrokerRejectionLogsTheKafkaErrorCodeAndReasonButNotTheMessage()
    {
        var completedEvent = NewEvent(Guid.NewGuid(), Guid.NewGuid());
        var logger = new RecordingLogger<KafkaConsultationCompletedPublisher>();
        var publisher = CreatePublisher(new Error(ErrorCode.Local_AllBrokersDown, "all brokers are down"), logger);

        var published = await publisher.PublishAsync(completedEvent);

        Assert.False(published);
        var entry = Assert.Single(logger.Errors);
        Assert.Contains("Failed to publish consultation-completed", entry.Message);
        Assert.Contains($"eventId={completedEvent.EventId}", entry.Message);
        Assert.Contains("errorType=ProduceException", entry.Message);
        Assert.Contains("kafkaErrorCode=Local_AllBrokersDown", entry.Message);
        Assert.Contains("kafkaErrorReason=all brokers are down", entry.Message);
        // The consultation and patient identifiers travel in the message, not in the log line.
        Assert.DoesNotContain(completedEvent.PatientId.ToString(), entry.Message);
        Assert.Null(entry.Exception);
    }

    // The reason comes from outside the service, so it cannot be allowed to forge log lines.
    [Fact]
    public async Task KafkaErrorReasonCannotAddALogLine()
    {
        var logger = new RecordingLogger<KafkaConsultationCompletedPublisher>();
        var publisher = CreatePublisher(new Error(ErrorCode.Local_Transport, "down\r\nfail: forged entry"), logger);

        await publisher.PublishAsync(NewEvent(Guid.NewGuid(), Guid.NewGuid()));

        var entry = Assert.Single(logger.Errors);
        Assert.DoesNotContain('\r', entry.Message);
        Assert.DoesNotContain('\n', entry.Message);
        Assert.Contains("kafkaErrorReason=downfail: forged entry", entry.Message);
    }

    private static KafkaConsultationCompletedPublisher CreatePublisher(
        Error error,
        RecordingLogger<KafkaConsultationCompletedPublisher> logger)
    {
        var producer = new Mock<IProducer<string, string>>();
        producer.Setup(candidate => candidate.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, string>(error, new DeliveryResult<string, string>()));

        return new KafkaConsultationCompletedPublisher(
            producer.Object,
            Options.Create(new KafkaCompletionOptions()),
            logger);
    }

    private static ConsultationCompletedEvent NewEvent(Guid consultationId, Guid doctorId) =>
        new(Guid.NewGuid(), consultationId, Guid.NewGuid(), Guid.NewGuid(), doctorId, "Viral URTI");

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    // Keeps what was logged, including whether an exception object was handed to the logger.
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Errors =>
            _entries.Where(entry => entry.Level == LogLevel.Error).ToList();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
