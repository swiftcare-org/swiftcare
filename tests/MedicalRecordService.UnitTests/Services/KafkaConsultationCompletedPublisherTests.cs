using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MedicalRecordService.Models.Events;
using MedicalRecordService.Services;

namespace MedicalRecordService.UnitTests.Services;

// The Kafka producer is replaced with a mock, so publishing, broker failure and timeout
// can be tested without a broker (SWC-151 mutation testing).
public class KafkaConsultationCompletedPublisherTests
{
    [Fact]
    public async Task PublishesTheEventToTheConfiguredTopicKeyedByQueue()
    {
        var completedEvent = NewEvent();
        string? topic = null;
        Message<string, string>? message = null;
        var producer = new Mock<IProducer<string, string>>();
        producer.Setup(candidate => candidate.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((sentTopic, sentMessage, _) =>
            {
                topic = sentTopic;
                message = sentMessage;
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        var published = await CreatePublisher(producer).PublishAsync(completedEvent);

        Assert.True(published);
        Assert.Equal("consultation-completed", topic);
        Assert.Equal(completedEvent.QueueId.ToString(), message!.Key);
        Assert.Equal(completedEvent, JsonSerializer.Deserialize<ConsultationCompletedEvent>(message.Value));
    }

    [Fact]
    public async Task BrokerRejectionReturnsFalse()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer.Setup(candidate => candidate.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, string>(
                new Error(ErrorCode.Local_AllBrokersDown),
                new DeliveryResult<string, string>()));

        Assert.False(await CreatePublisher(producer).PublishAsync(NewEvent()));
    }

    [Fact]
    public async Task BrokerThatNeverAnswersTimesOutAndReturnsFalse()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer.Setup(candidate => candidate.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Message<string, string>, CancellationToken>(async (_, _, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new DeliveryResult<string, string>();
            });

        Assert.False(await CreatePublisher(producer, messageTimeoutMs: 50).PublishAsync(NewEvent()));
    }

    [Fact]
    public async Task NullEventIsRejectedBeforeProducing()
    {
        var producer = new Mock<IProducer<string, string>>(MockBehavior.Strict);

        await Assert.ThrowsAsync<ArgumentNullException>(() => CreatePublisher(producer).PublishAsync(null!));
    }

    [Fact]
    public void DefaultTopicMatchesTheTopicQueueServiceConsumes()
    {
        var options = new KafkaCompletionOptions();

        Assert.Equal("consultation-completed", options.ConsultationCompletedTopic);
        Assert.Equal(string.Empty, options.BootstrapServers);
    }

    private static KafkaConsultationCompletedPublisher CreatePublisher(
        Mock<IProducer<string, string>> producer,
        int messageTimeoutMs = 5000) => new(
            producer.Object,
            Options.Create(new KafkaCompletionOptions { MessageTimeoutMs = messageTimeoutMs }),
            NullLogger<KafkaConsultationCompletedPublisher>.Instance);

    private static ConsultationCompletedEvent NewEvent() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
}
