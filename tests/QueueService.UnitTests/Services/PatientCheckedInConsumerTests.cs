using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using QueueService.Models.Configuration;
using QueueService.Models.Dtos;
using QueueService.Models.Enums;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

public class PatientCheckedInConsumerTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    private static KafkaOptions DefaultKafkaOptions() => new()
    {
        BootstrapServers = "localhost:9092",
        PatientCheckedInTopic = "patient-checked-in",
        ConsumerGroupId = "queue-service",
        // Kept short so the failure-path test (which waits out one retry delay) stays fast.
        RetryDelay = TimeSpan.FromMilliseconds(10)
    };

    private static ConsumeResult<string, string> BuildResult(string value, string? correlationId = "test-correlation-id")
    {
        var headers = new Headers();
        if (correlationId is not null)
        {
            headers.Add("X-Correlation-ID", Encoding.UTF8.GetBytes(correlationId));
        }

        return new ConsumeResult<string, string>
        {
            Topic = "patient-checked-in",
            Partition = new Partition(0),
            Offset = new Offset(0),
            Message = new Message<string, string>
            {
                Key = Guid.NewGuid().ToString(),
                Value = value,
                Headers = headers
            }
        };
    }

    // The mocked Consume() sequence returns the one message under test, then throws
    // OperationCanceledException on the next call - this ends PatientCheckedInConsumer's
    // loop deterministically after exactly one message, without relying on real
    // cancellation (a real StopAsync() called immediately after StartAsync() races the
    // BackgroundService's own Task.Yield() startup and can cancel the loop before it ever
    // reaches Consume(), which is why the tests below wait on a callback signal instead of
    // just calling StopAsync() right away).
    private static Mock<IConsumer<string, string>> CreateConsumerMock(ConsumeResult<string, string> result)
    {
        var consumerMock = new Mock<IConsumer<string, string>>();
        consumerMock
            .SetupSequence(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(result)
            .Throws<OperationCanceledException>();
        return consumerMock;
    }

    private static IServiceScopeFactory ScopeFactoryFor(IQueueEntryCreationService creationService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(creationService);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static PatientCheckedInConsumer CreateConsumer(
        Mock<IConsumer<string, string>> consumerMock,
        IQueueEntryCreationService creationService,
        KafkaOptions? options = null) =>
        new(
            consumerMock.Object,
            ScopeFactoryFor(creationService),
            Options.Create(options ?? DefaultKafkaOptions()),
            NullLogger<PatientCheckedInConsumer>.Instance);

    private static TaskCompletionSource ArmSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task RunAndWaitAsync(PatientCheckedInConsumer consumer, TaskCompletionSource signal)
    {
        await consumer.StartAsync(CancellationToken.None);
        await signal.Task.WaitAsync(SignalTimeout);
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ValidEvent_IsDispatchedToCreationServiceAndOffsetIsCommitted()
    {
        var eventId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var checkedInAt = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            EventId = eventId,
            PatientId = patientId,
            IsNewPatient = true,
            CheckedInAt = checkedInAt,
            CorrelationId = "corr-abc"
        });
        var result = BuildResult(payload);
        var consumerMock = CreateConsumerMock(result);

        var signal = ArmSignal();
        consumerMock.Setup(c => c.Commit(It.IsAny<ConsumeResult<string, string>>())).Callback(() => signal.TrySetResult());

        var creationServiceMock = new Mock<IQueueEntryCreationService>();
        creationServiceMock
            .Setup(s => s.CreateQueueEntryAsync(eventId, patientId, checkedInAt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueEntryCreationResult { Outcome = QueueEntryCreationOutcome.Created, QueueNumber = "Q-001" });

        var consumer = CreateConsumer(consumerMock, creationServiceMock.Object);
        await RunAndWaitAsync(consumer, signal);

        creationServiceMock.Verify(
            s => s.CreateQueueEntryAsync(eventId, patientId, checkedInAt, It.IsAny<CancellationToken>()),
            Times.Once);
        consumerMock.Verify(c => c.Commit(result), Times.Once);
        consumerMock.Verify(c => c.Seek(It.IsAny<TopicPartitionOffset>()), Times.Never);
    }

    [Fact]
    public async Task MalformedPayload_IsSkippedAndCommittedWithoutCallingCreationService()
    {
        var result = BuildResult("{ this is not valid json");
        var consumerMock = CreateConsumerMock(result);

        var signal = ArmSignal();
        consumerMock.Setup(c => c.Commit(It.IsAny<ConsumeResult<string, string>>())).Callback(() => signal.TrySetResult());

        var creationServiceMock = new Mock<IQueueEntryCreationService>();

        var consumer = CreateConsumer(consumerMock, creationServiceMock.Object);
        await RunAndWaitAsync(consumer, signal);

        creationServiceMock.Verify(
            s => s.CreateQueueEntryAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        consumerMock.Verify(c => c.Commit(result), Times.Once);
    }

    [Fact]
    public async Task EmptyEventId_IsSkippedAndCommittedWithoutCallingCreationService()
    {
        var payload = JsonSerializer.Serialize(new
        {
            EventId = Guid.Empty,
            PatientId = Guid.NewGuid(),
            IsNewPatient = true,
            CheckedInAt = DateTime.UtcNow,
            CorrelationId = "corr-abc"
        });
        var result = BuildResult(payload);
        var consumerMock = CreateConsumerMock(result);

        var signal = ArmSignal();
        consumerMock.Setup(c => c.Commit(It.IsAny<ConsumeResult<string, string>>())).Callback(() => signal.TrySetResult());

        var creationServiceMock = new Mock<IQueueEntryCreationService>();

        var consumer = CreateConsumer(consumerMock, creationServiceMock.Object);
        await RunAndWaitAsync(consumer, signal);

        creationServiceMock.Verify(
            s => s.CreateQueueEntryAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        consumerMock.Verify(c => c.Commit(result), Times.Once);
    }

    [Fact]
    public async Task CreationServiceFailure_DoesNotCommitAndSeeksBackToRetry()
    {
        var payload = JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            IsNewPatient = true,
            CheckedInAt = DateTime.UtcNow,
            CorrelationId = "corr-abc"
        });
        var result = BuildResult(payload);
        var consumerMock = CreateConsumerMock(result);

        var signal = ArmSignal();
        consumerMock.Setup(c => c.Seek(It.IsAny<TopicPartitionOffset>())).Callback(() => signal.TrySetResult());

        var creationServiceMock = new Mock<IQueueEntryCreationService>();
        creationServiceMock
            .Setup(s => s.CreateQueueEntryAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated database failure"));

        var consumer = CreateConsumer(consumerMock, creationServiceMock.Object);
        await RunAndWaitAsync(consumer, signal);

        consumerMock.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
        consumerMock.Verify(c => c.Seek(result.TopicPartitionOffset), Times.Once);
    }
}
