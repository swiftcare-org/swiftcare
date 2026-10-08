using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NotificationService.Models.Configuration;
using NotificationService.Models.Entities;
using NotificationService.Models.Enums;
using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

// SWC-143: the Kafka consumer stores each event before committing it, skips what it
// cannot read, and retries what it could not store.
public class ActivityEventConsumerTests
{
    private const string SensitiveMessage = "Duplicate entry for patient Nimal Perera, NIC 199012345678";

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 5, 0, 0, TimeSpan.Zero);

    private static readonly string[] StoreThenCommit = ["store", "commit"];

    private static readonly string[] AllTopics = ["patient-checked-in", "patient-called", "consultation-completed"];

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task EventIsStoredBeforeItsOffsetIsCommitted()
    {
        var eventId = Guid.NewGuid();
        var result = BuildResult("patient-checked-in", CheckedInPayload(eventId));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        var order = new List<string>();
        Notification? stored = null;
        var recorder = new Mock<INotificationRecorder>(MockBehavior.Strict);
        recorder.Setup(item => item.RecordAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification notification, CancellationToken _) =>
            {
                stored = notification;
                order.Add("store");
                return RecordNotificationOutcome.Recorded;
            });
        consumer.Setup(item => item.Commit(result)).Callback(() =>
        {
            order.Add("commit");
            signal.TrySetResult();
        });

        await RunOnceAsync(consumer, recorder, signal);

        Assert.Equal(StoreThenCommit, order);
        Assert.NotNull(stored);
        Assert.Equal(eventId, stored.EventId);
        Assert.Equal(NotificationType.PatientCheckedIn, stored.Type);
        Assert.Equal(Now.UtcDateTime, stored.ReceivedAt);
        consumer.Verify(item => item.Seek(It.IsAny<TopicPartitionOffset>()), Times.Never);
    }

    [Theory]
    [InlineData("patient-checked-in", NotificationType.PatientCheckedIn)]
    [InlineData("patient-called", NotificationType.PatientCalled)]
    [InlineData("consultation-completed", NotificationType.ConsultationCompleted)]
    public async Task EachTopicIsStoredAsItsOwnNotificationType(string topic, NotificationType expectedType)
    {
        var result = BuildResult(topic, PayloadFor(topic));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        Notification? stored = null;
        var recorder = new Mock<INotificationRecorder>();
        recorder.Setup(item => item.RecordAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((notification, _) => stored = notification)
            .ReturnsAsync(RecordNotificationOutcome.Recorded);
        consumer.Setup(item => item.Commit(result)).Callback(() => signal.TrySetResult());

        await RunOnceAsync(consumer, recorder, signal);

        Assert.Equal(expectedType, stored!.Type);
    }

    [Fact]
    public async Task ConsumerSubscribesToAllThreeTopics()
    {
        var result = BuildResult("patient-checked-in", CheckedInPayload(Guid.NewGuid()));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(result)).Callback(() => signal.TrySetResult());

        await RunOnceAsync(consumer, RecorderReturning(RecordNotificationOutcome.Recorded), signal);

        consumer.Verify(
            item => item.Subscribe(It.Is<IEnumerable<string>>(topics => topics.SequenceEqual(AllTopics))),
            Times.Once);
    }

    // A redelivered event is already stored. Its offset is still committed, or it would
    // be delivered forever.
    [Fact]
    public async Task DuplicateEventIsCommittedWithoutARetry()
    {
        var result = BuildResult("patient-checked-in", CheckedInPayload(Guid.NewGuid()));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(result)).Callback(() => signal.TrySetResult());

        await RunOnceAsync(consumer, RecorderReturning(RecordNotificationOutcome.Duplicate), signal);

        consumer.Verify(item => item.Commit(result), Times.Once);
        consumer.Verify(item => item.Seek(It.IsAny<TopicPartitionOffset>()), Times.Never);
    }

    [Theory]
    [InlineData("{invalid json")]
    [InlineData("{}")]
    public async Task InvalidEventIsSkippedAndCommittedWithoutBeingStored(string payload)
    {
        var result = BuildResult("patient-checked-in", payload);
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(result)).Callback(() => signal.TrySetResult());
        var recorder = new Mock<INotificationRecorder>(MockBehavior.Strict);
        var logger = new RecordingLogger();

        await RunOnceAsync(consumer, recorder, signal, logger);

        recorder.VerifyNoOtherCalls();
        consumer.Verify(item => item.Commit(result), Times.Once);
        var entry = Assert.Single(logger.Errors);
        Assert.Contains("Invalid activity event; skipping message", entry.Message);
        Assert.Contains("topic=patient-checked-in", entry.Message);
        Assert.Contains("position=42", entry.Message);
        // The payload is not known to be safe, so it is never written to the log.
        Assert.DoesNotContain(payload, entry.Message);
    }

    [Fact]
    public async Task MessageWithoutAValueIsIgnored()
    {
        var empty = BuildResult("patient-checked-in", null);
        var valid = BuildResult("patient-checked-in", CheckedInPayload(Guid.NewGuid()));
        var consumer = new Mock<IConsumer<string, string>>();
        consumer.SetupSequence(item => item.Consume(It.IsAny<CancellationToken>()))
            .Returns(empty)
            .Returns(valid)
            .Throws<OperationCanceledException>();
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(valid)).Callback(() => signal.TrySetResult());

        await RunOnceAsync(consumer, RecorderReturning(RecordNotificationOutcome.Recorded), signal);

        consumer.Verify(item => item.Commit(empty), Times.Never);
        consumer.Verify(item => item.Commit(valid), Times.Once);
    }

    [Fact]
    public async Task StorageFailureRereadsTheSameEventWithoutCommitting()
    {
        var eventId = Guid.NewGuid();
        var result = BuildResult("patient-checked-in", CheckedInPayload(eventId));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Seek(result.TopicPartitionOffset)).Callback(() => signal.TrySetResult());
        var recorder = new Mock<INotificationRecorder>();
        recorder.Setup(item => item.RecordAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(SensitiveMessage));
        var logger = new RecordingLogger();

        await RunOnceAsync(consumer, recorder, signal, logger);

        consumer.Verify(item => item.Seek(result.TopicPartitionOffset), Times.Once);
        consumer.Verify(item => item.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
        var entry = Assert.Single(logger.Errors);
        Assert.Contains("Storing activity event failed", entry.Message);
        Assert.Contains($"eventId={eventId}", entry.Message);
        Assert.Contains("errorType=InvalidOperationException", entry.Message);
        // Only the failure type is logged: the message could carry patient data.
        Assert.DoesNotContain("Nimal Perera", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task OffsetCommitFailureIsLoggedWithTheKafkaErrorAndDoesNotStopTheConsumer()
    {
        var result = BuildResult("patient-checked-in", CheckedInPayload(Guid.NewGuid()));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(result))
            .Callback(() => signal.TrySetResult())
            .Throws(new KafkaException(new Error(ErrorCode.Local_Transport, "broker\r\ntransport failure")));
        var logger = new RecordingLogger();

        await RunOnceAsync(consumer, RecorderReturning(RecordNotificationOutcome.Recorded), signal, logger);

        var entry = Assert.Single(logger.Errors);
        Assert.Contains("Failed to commit activity event offset", entry.Message);
        Assert.Contains("position=42", entry.Message);
        Assert.Contains("errorType=KafkaException", entry.Message);
        Assert.Contains("kafkaErrorCode=Local_Transport", entry.Message);
        Assert.Contains("kafkaErrorReason=brokertransport failure", entry.Message);
        Assert.DoesNotContain('\n', entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task ConsumeErrorIsLoggedAndTheNextEventIsStillHandled()
    {
        var valid = BuildResult("patient-checked-in", CheckedInPayload(Guid.NewGuid()));
        var consumer = new Mock<IConsumer<string, string>>();
        consumer.SetupSequence(item => item.Consume(It.IsAny<CancellationToken>()))
            .Throws(new ConsumeException(
                new ConsumeResult<byte[], byte[]>(),
                new Error(ErrorCode.Local_AllBrokersDown, "all brokers are down")))
            .Returns(valid)
            .Throws<OperationCanceledException>();
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(valid)).Callback(() => signal.TrySetResult());
        var logger = new RecordingLogger();

        await RunOnceAsync(consumer, RecorderReturning(RecordNotificationOutcome.Recorded), signal, logger);

        consumer.Verify(item => item.Commit(valid), Times.Once);
        var entry = Assert.Single(logger.Errors);
        Assert.Contains("Kafka consume error on activity topics", entry.Message);
        Assert.Contains("kafkaErrorCode=Local_AllBrokersDown", entry.Message);
        Assert.Contains("kafkaErrorReason=all brokers are down", entry.Message);
    }

    [Fact]
    public async Task StoppingTheWorkerClosesTheKafkaConsumer()
    {
        var result = BuildResult("patient-checked-in", CheckedInPayload(Guid.NewGuid()));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(result)).Callback(() => signal.TrySetResult());

        await RunOnceAsync(consumer, RecorderReturning(RecordNotificationOutcome.Recorded), signal);

        consumer.Verify(item => item.Close(), Times.Once);
        consumer.Verify(item => item.Dispose(), Times.Once);
    }

    private static string CheckedInPayload(Guid eventId) => JsonSerializer.Serialize(new
    {
        EventId = eventId,
        PatientId = Guid.NewGuid(),
        IsNewPatient = true,
        CheckedInAt = new DateTime(2026, 10, 8, 4, 30, 0, DateTimeKind.Utc),
        CorrelationId = "correlation"
    });

    private static string PayloadFor(string topic) => topic switch
    {
        "patient-called" => JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            QueueId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            QueueNumber = "Q-007",
            DoctorId = Guid.NewGuid(),
            DoctorName = "Dr. Silva",
            RoomNumber = "1",
            CalledAt = new DateTime(2026, 10, 8, 4, 31, 0, DateTimeKind.Utc)
        }),
        "consultation-completed" => JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            ConsultationId = Guid.NewGuid(),
            QueueId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            DoctorId = Guid.NewGuid()
        }),
        _ => CheckedInPayload(Guid.NewGuid())
    };

    private static ConsumeResult<string, string> BuildResult(string topic, string? payload) => new()
    {
        Topic = topic,
        Partition = new Partition(0),
        Offset = new Offset(42),
        Message = new Message<string, string> { Key = Guid.NewGuid().ToString(), Value = payload! }
    };

    private static Mock<IConsumer<string, string>> CreateConsumerMock(ConsumeResult<string, string> result)
    {
        var consumer = new Mock<IConsumer<string, string>>();
        consumer.SetupSequence(item => item.Consume(It.IsAny<CancellationToken>()))
            .Returns(result)
            .Throws<OperationCanceledException>();
        return consumer;
    }

    private static Mock<INotificationRecorder> RecorderReturning(RecordNotificationOutcome outcome)
    {
        var recorder = new Mock<INotificationRecorder>();
        recorder.Setup(item => item.RecordAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        return recorder;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task RunOnceAsync(
        Mock<IConsumer<string, string>> consumer,
        Mock<INotificationRecorder> recorder,
        TaskCompletionSource signal,
        RecordingLogger? logger = null)
    {
        using var provider = new ServiceCollection()
            .AddSingleton(recorder.Object)
            .BuildServiceProvider();
        var worker = new ActivityEventConsumer(
            consumer.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions
            {
                BootstrapServers = "localhost:9092",
                RetryDelay = TimeSpan.FromMilliseconds(10)
            }),
            new FixedTimeProvider(),
            logger ?? new RecordingLogger());

        await worker.StartAsync(CancellationToken.None);
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Stopping waits for the worker loop to finish, so every log line is written by then.
        await worker.StopAsync(CancellationToken.None);
        worker.Dispose();
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    // Keeps what was logged, including whether an exception object was handed to the logger.
    private sealed class RecordingLogger : ILogger<ActivityEventConsumer>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Errors
        {
            get
            {
                lock (_entries)
                {
                    return _entries.Where(entry => entry.Level == LogLevel.Error).ToList();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
            }
        }
    }
}
