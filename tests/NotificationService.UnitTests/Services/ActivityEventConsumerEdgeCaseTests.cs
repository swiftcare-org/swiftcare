using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NotificationService.Models.Configuration;
using NotificationService.Models.Entities;
using NotificationService.Services;

namespace NotificationService.UnitTests.Services;

// The pause before an event that could not be stored is read again
// (SWC-151 mutation testing).
public class ActivityEventConsumerEdgeCaseTests
{
    [Fact]
    public async Task StorageFailureWaitsTheRetryDelayBeforeReadingTheEventAgain()
    {
        var result = new ConsumeResult<string, string>
        {
            Topic = "patient-checked-in",
            Partition = new Partition(0),
            Offset = new Offset(42),
            Message = new Message<string, string> { Key = "key", Value = CheckedInPayload() }
        };
        var reads = 0;
        var readAgain = NewSignal();
        var consumer = new Mock<IConsumer<string, string>>();
        consumer.Setup(item => item.Consume(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(token =>
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    return result;
                }

                readAgain.TrySetResult();
                token.WaitHandle.WaitOne();
                throw new OperationCanceledException(token);
            });
        var seeked = NewSignal();
        consumer.Setup(item => item.Seek(result.TopicPartitionOffset)).Callback(() => seeked.TrySetResult());
        var recorder = new Mock<INotificationRecorder>();
        recorder.Setup(item => item.RecordAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        using var provider = new ServiceCollection().AddSingleton(recorder.Object).BuildServiceProvider();
        var worker = new ActivityEventConsumer(
            consumer.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaOptions
            {
                BootstrapServers = "localhost:9092",
                RetryDelay = TimeSpan.FromMinutes(10)
            }),
            TimeProvider.System,
            NullLogger<ActivityEventConsumer>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await seeked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var readDuringTheDelay =
            await Task.WhenAny(readAgain.Task, Task.Delay(TimeSpan.FromMilliseconds(500))) == readAgain.Task;
        await worker.StopAsync(CancellationToken.None);
        worker.Dispose();

        Assert.False(readDuringTheDelay);
        Assert.Equal(1, Volatile.Read(ref reads));
        recorder.Verify(
            item => item.RecordAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static string CheckedInPayload() => JsonSerializer.Serialize(new
    {
        EventId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        IsNewPatient = true,
        CheckedInAt = new DateTime(2026, 10, 8, 4, 30, 0, DateTimeKind.Utc),
        CorrelationId = "correlation"
    });

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
