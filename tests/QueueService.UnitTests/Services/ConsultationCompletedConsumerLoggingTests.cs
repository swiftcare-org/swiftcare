using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using QueueService.Models.Configuration;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

// Issue #121: a swallowed failure must say what kind of failure it was, without ever
// writing the exception message, which could carry patient data, to the log.
public class ConsultationCompletedConsumerLoggingTests
{
    private const string SensitiveMessage = "Duplicate entry for patient Nimal Perera, NIC 199012345678";

    [Fact]
    public async Task ProcessingFailureLogsTheExceptionTypeButNotItsMessage()
    {
        var completedEvent = NewEvent();
        var result = BuildResult(JsonSerializer.Serialize(completedEvent));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Seek(result.TopicPartitionOffset)).Callback(() => signal.TrySetResult());
        var service = new Mock<IQueueCompletionService>();
        service.Setup(item => item.CompleteAsync(completedEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(SensitiveMessage));
        var logger = new RecordingLogger();

        await RunOnceAsync(consumer, service, signal, logger);

        var entry = Assert.Single(logger.Errors);
        Assert.Contains("Consultation-completed processing failed", entry.Message);
        Assert.Contains($"eventId={completedEvent.EventId}", entry.Message);
        Assert.Contains("errorType=InvalidOperationException", entry.Message);
        Assert.DoesNotContain(SensitiveMessage, entry.Message);
        Assert.DoesNotContain("Nimal Perera", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task OffsetCommitFailureLogsTheKafkaErrorCodeAndReason()
    {
        var result = BuildResult("{invalid json");
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(result))
            .Callback(() => signal.TrySetResult())
            .Throws(new KafkaException(new Error(ErrorCode.Local_Transport, "broker transport failure")));
        var service = new Mock<IQueueCompletionService>(MockBehavior.Strict);
        var logger = new RecordingLogger();

        await RunOnceAsync(consumer, service, signal, logger);

        var entry = Assert.Single(logger.Errors, error => error.Message.StartsWith("Failed to commit"));
        Assert.Contains("errorType=KafkaException", entry.Message);
        Assert.Contains("kafkaErrorCode=Local_Transport", entry.Message);
        Assert.Contains("kafkaErrorReason=broker transport failure", entry.Message);
        Assert.Null(entry.Exception);
    }

    // The reason comes from outside the service, so it cannot be allowed to forge log lines.
    [Fact]
    public async Task KafkaErrorReasonCannotAddALogLine()
    {
        var result = BuildResult("{invalid json");
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(result))
            .Callback(() => signal.TrySetResult())
            .Throws(new KafkaException(new Error(ErrorCode.Local_Transport, "down\r\nfail: forged entry")));
        var logger = new RecordingLogger();

        await RunOnceAsync(consumer, new Mock<IQueueCompletionService>(MockBehavior.Strict), signal, logger);

        var entry = Assert.Single(logger.Errors, error => error.Message.StartsWith("Failed to commit"));
        Assert.DoesNotContain('\r', entry.Message);
        Assert.DoesNotContain('\n', entry.Message);
        Assert.Contains("kafkaErrorReason=downfail: forged entry", entry.Message);
    }

    private static ConsultationCompletedEvent NewEvent() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static ConsumeResult<string, string> BuildResult(string payload) => new()
    {
        Topic = "consultation-completed",
        Partition = new Partition(0),
        Offset = new Offset(0),
        Message = new Message<string, string> { Key = Guid.NewGuid().ToString(), Value = payload }
    };

    private static Mock<IConsumer<string, string>> CreateConsumerMock(ConsumeResult<string, string> result)
    {
        var consumer = new Mock<IConsumer<string, string>>();
        consumer.SetupSequence(item => item.Consume(It.IsAny<CancellationToken>()))
            .Returns(result)
            .Throws<OperationCanceledException>();
        return consumer;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task RunOnceAsync(
        Mock<IConsumer<string, string>> consumer,
        Mock<IQueueCompletionService> service,
        TaskCompletionSource signal,
        RecordingLogger logger)
    {
        using var provider = new ServiceCollection()
            .AddSingleton(service.Object)
            .BuildServiceProvider();
        using var worker = new ConsultationCompletedConsumer(
            consumer.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions
            {
                BootstrapServers = "localhost:9092",
                PatientCheckedInTopic = "patient-checked-in",
                PatientCalledTopic = "patient-called",
                ConsumerGroupId = "queue-service",
                RetryDelay = TimeSpan.FromMilliseconds(10)
            }),
            logger);

        await worker.StartAsync(CancellationToken.None);
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Stopping waits for the worker loop to finish, so every log line is written by then.
        await worker.StopAsync(CancellationToken.None);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    // Keeps what was logged, including whether an exception object was handed to the logger.
    private sealed class RecordingLogger : ILogger<ConsultationCompletedConsumer>
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
