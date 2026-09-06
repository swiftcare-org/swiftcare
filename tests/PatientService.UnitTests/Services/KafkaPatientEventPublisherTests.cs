using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PatientService.Models.Configuration;
using PatientService.Models.Events;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public class KafkaPatientEventPublisherTests
{
    private static KafkaOptions Options() => new()
    {
        BootstrapServers = "localhost:9092",
        PatientCheckedInTopic = "patient-checked-in",
        MessageTimeoutMs = 5000
    };

    private static KafkaPatientEventPublisher CreatePublisher(
        Mock<IProducer<string, string>> producerMock,
        KafkaOptions? options = null)
    {
        return new KafkaPatientEventPublisher(
            producerMock.Object,
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            NullLogger<KafkaPatientEventPublisher>.Instance);
    }

    [Fact]
    public async Task PublishesToTheConfiguredTopicWithPatientIdAsKey()
    {
        var producerMock = new Mock<IProducer<string, string>>();
        Message<string, string>? capturedMessage = null;
        string? capturedTopic = null;

        producerMock
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, message, _) =>
            {
                capturedTopic = topic;
                capturedMessage = message;
            })
            .ReturnsAsync(new DeliveryResult<string, string>());

        var publisher = CreatePublisher(producerMock);
        var patientId = Guid.NewGuid();

        var result = await publisher.PublishPatientCheckedInAsync(patientId, isNewPatient: true, correlationId: "corr-123");

        Assert.True(result);
        Assert.Equal("patient-checked-in", capturedTopic);
        Assert.Equal(patientId.ToString(), capturedMessage!.Key);
    }

    [Fact]
    public async Task PublishedEventHasIsNewPatientTrueAndNoPatientDetailFields()
    {
        var producerMock = new Mock<IProducer<string, string>>();
        Message<string, string>? capturedMessage = null;

        producerMock
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((_, message, _) => capturedMessage = message)
            .ReturnsAsync(new DeliveryResult<string, string>());

        var publisher = CreatePublisher(producerMock);
        var patientId = Guid.NewGuid();

        await publisher.PublishPatientCheckedInAsync(patientId, isNewPatient: true, correlationId: "corr-123");

        var payload = JsonSerializer.Deserialize<PatientCheckedInEvent>(capturedMessage!.Value)!;
        Assert.Equal(patientId, payload.PatientId);
        Assert.True(payload.IsNewPatient);
        Assert.Equal("corr-123", payload.CorrelationId);

        // No patient-identifying fields must ever appear on the wire for this event.
        Assert.DoesNotContain("nic", capturedMessage.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullName", capturedMessage.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bloodGroup", capturedMessage.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReturningPatientEventHasIsNewPatientFalse()
    {
        var producerMock = new Mock<IProducer<string, string>>();
        Message<string, string>? capturedMessage = null;

        producerMock
            .Setup(producer => producer.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>(
                (_, message, _) => capturedMessage = message)
            .ReturnsAsync(new DeliveryResult<string, string>());

        var publisher = CreatePublisher(producerMock);
        var patientId = Guid.NewGuid();

        await publisher.PublishPatientCheckedInAsync(
            patientId,
            isNewPatient: false,
            correlationId: "returning-correlation-id");

        var payload = JsonSerializer.Deserialize<PatientCheckedInEvent>(capturedMessage!.Value)!;
        Assert.Equal(patientId, payload.PatientId);
        Assert.False(payload.IsNewPatient);
        Assert.Equal("returning-correlation-id", payload.CorrelationId);
    }

    [Fact]
    public async Task CorrelationIdIsAttachedAsAMessageHeader()
    {
        var producerMock = new Mock<IProducer<string, string>>();
        Message<string, string>? capturedMessage = null;

        producerMock
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((_, message, _) => capturedMessage = message)
            .ReturnsAsync(new DeliveryResult<string, string>());

        var publisher = CreatePublisher(producerMock);

        await publisher.PublishPatientCheckedInAsync(Guid.NewGuid(), isNewPatient: true, correlationId: "corr-abc");

        var header = capturedMessage!.Headers.Single(h => h.Key == "X-Correlation-ID");
        Assert.Equal("corr-abc", Encoding.UTF8.GetString(header.GetValueBytes()));
    }

    [Fact]
    public async Task ProduceExceptionIsCaughtAndSurfacedAsAFailureResultRatherThanThrown()
    {
        var producerMock = new Mock<IProducer<string, string>>();
        producerMock
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, string>(
                new Error(ErrorCode.Local_MsgTimedOut, "Simulated broker failure"),
                new DeliveryResult<string, string>()));

        var publisher = CreatePublisher(producerMock);

        var result = await publisher.PublishPatientCheckedInAsync(Guid.NewGuid(), isNewPatient: true, correlationId: "corr-123");

        Assert.False(result);
    }

    [Fact]
    public async Task UnreachableBrokerTimesOutAndReturnsFailureRatherThanHanging()
    {
        var producerMock = new Mock<IProducer<string, string>>();
        producerMock
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, Message<string, string> _, CancellationToken token) =>
            {
                // Simulates an unreachable broker: awaits forever unless cancelled by the
                // publisher's own MessageTimeoutMs-bounded CancellationTokenSource.
                await Task.Delay(Timeout.Infinite, token);
                return new DeliveryResult<string, string>();
            });

        var options = Options();
        options.MessageTimeoutMs = 100;
        var fastTimeoutPublisher = CreatePublisher(producerMock, options);

        var result = await fastTimeoutPublisher.PublishPatientCheckedInAsync(Guid.NewGuid(), isNewPatient: true, correlationId: "corr-123");

        Assert.False(result);
    }
}
