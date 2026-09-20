using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using QueueService.Models.Configuration;
using QueueService.Models.Enums;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

public class ConsultationCompletedConsumerTests
{
    [Fact]
    public async Task ValidEventIsProcessedBeforeOffsetIsCommitted()
    {
        var completedEvent = NewEvent();
        var result = BuildResult(JsonSerializer.Serialize(completedEvent));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        var order = new List<string>();
        var service = new Mock<IQueueCompletionService>(MockBehavior.Strict);
        service.Setup(item => item.CompleteAsync(completedEvent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Add("queue");
                return QueueCompletionOutcome.Completed;
            });
        consumer.Setup(item => item.Commit(result)).Callback(() =>
        {
            order.Add("offset");
            signal.TrySetResult();
        });

        await RunOnceAsync(consumer, service, signal);

        Assert.Equal(new[] { "queue", "offset" }, order);
        consumer.Verify(item => item.Seek(It.IsAny<TopicPartitionOffset>()), Times.Never);
    }

    [Theory]
    [InlineData("{invalid json")]
    [InlineData("{}")]
    public async Task InvalidEventIsSkippedWithoutChangingQueue(string payload)
    {
        var result = BuildResult(payload);
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Commit(result)).Callback(() => signal.TrySetResult());
        var service = new Mock<IQueueCompletionService>(MockBehavior.Strict);

        await RunOnceAsync(consumer, service, signal);

        service.VerifyNoOtherCalls();
        consumer.Verify(item => item.Commit(result), Times.Once);
    }

    [Fact]
    public async Task QueueFailureSeeksSameMessageWithoutCommittingOffset()
    {
        var completedEvent = NewEvent();
        var result = BuildResult(JsonSerializer.Serialize(completedEvent));
        var consumer = CreateConsumerMock(result);
        var signal = NewSignal();
        consumer.Setup(item => item.Seek(result.TopicPartitionOffset))
            .Callback(() => signal.TrySetResult());
        var service = new Mock<IQueueCompletionService>(MockBehavior.Strict);
        service.Setup(item => item.CompleteAsync(completedEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        await RunOnceAsync(consumer, service, signal);

        consumer.Verify(item => item.Seek(result.TopicPartitionOffset), Times.Once);
        consumer.Verify(item => item.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
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

    private static Mock<IConsumer<string, string>> CreateConsumerMock(
        ConsumeResult<string, string> result)
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
        TaskCompletionSource signal)
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
            NullLogger<ConsultationCompletedConsumer>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }
}
