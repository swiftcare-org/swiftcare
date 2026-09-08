using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using QueueService.Models.Configuration;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

public class KafkaQueueEventPublisherTests
{
    [Fact]
    public async Task PublishesPatientCalledEventToConfiguredTopicWithCorrelationHeader()
    {
        var producer = new Mock<IProducer<string, string>>();
        string? capturedTopic = null;
        Message<string, string>? capturedMessage = null;
        producer
            .Setup(value => value.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, message, _) =>
            {
                capturedTopic = topic;
                capturedMessage = message;
            })
            .ReturnsAsync(new DeliveryResult<string, string>());
        var patientCalledEvent = NewEvent();

        var result = await CreatePublisher(producer).PublishPatientCalledAsync(patientCalledEvent);

        Assert.True(result);
        Assert.Equal("patient-called", capturedTopic);
        Assert.Equal(patientCalledEvent.QueueId.ToString(), capturedMessage!.Key);
        var payload = JsonSerializer.Deserialize<PatientCalledEvent>(capturedMessage.Value)!;
        Assert.Equal(patientCalledEvent.EventId, payload.EventId);
        Assert.Equal(patientCalledEvent.QueueId, payload.QueueId);
        Assert.Equal(patientCalledEvent.PatientId, payload.PatientId);
        Assert.Equal(patientCalledEvent.QueueNumber, payload.QueueNumber);
        Assert.Equal(patientCalledEvent.DoctorId, payload.DoctorId);
        Assert.Equal(patientCalledEvent.DoctorName, payload.DoctorName);
        Assert.Equal(patientCalledEvent.RoomNumber, payload.RoomNumber);
        Assert.Equal(patientCalledEvent.CalledAt, payload.CalledAt);
        Assert.Equal(patientCalledEvent.CorrelationId, payload.CorrelationId);
        var header = capturedMessage.Headers.Single(item => item.Key == "X-Correlation-ID");
        Assert.Equal(
            patientCalledEvent.CorrelationId,
            Encoding.UTF8.GetString(header.GetValueBytes()));
    }

    [Fact]
    public async Task ProduceExceptionReturnsFailure()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(value => value.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, string>(
                new Error(ErrorCode.Local_MsgTimedOut, "Simulated broker failure"),
                new DeliveryResult<string, string>()));

        var result = await CreatePublisher(producer).PublishPatientCalledAsync(NewEvent());

        Assert.False(result);
    }

    [Fact]
    public async Task UnreachableBrokerTimesOutAndReturnsFailure()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(value => value.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, Message<string, string> _, CancellationToken token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new DeliveryResult<string, string>();
            });

        var result = await CreatePublisher(producer, messageTimeoutMs: 100)
            .PublishPatientCalledAsync(NewEvent());

        Assert.False(result);
    }

    private static KafkaQueueEventPublisher CreatePublisher(
        Mock<IProducer<string, string>> producer,
        int messageTimeoutMs = 5000) => new(
            producer.Object,
            Options.Create(new KafkaOptions
            {
                BootstrapServers = "localhost:9092",
                PatientCheckedInTopic = "patient-checked-in",
                PatientCalledTopic = "patient-called",
                ConsumerGroupId = "queue-service",
                MessageTimeoutMs = messageTimeoutMs
            }),
            NullLogger<KafkaQueueEventPublisher>.Instance);

    private static PatientCalledEvent NewEvent() => new()
    {
        EventId = Guid.NewGuid(),
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        QueueNumber = "Q-007",
        DoctorId = Guid.NewGuid(),
        DoctorName = "Dr. Amara Chen",
        RoomNumber = "R-204",
        CalledAt = new DateTime(2026, 9, 8, 6, 30, 0, DateTimeKind.Utc),
        CorrelationId = "corr-patient-called"
    };
}
