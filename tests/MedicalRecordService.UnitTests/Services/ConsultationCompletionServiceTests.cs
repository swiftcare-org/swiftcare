using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Models.Events;
using MedicalRecordService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MedicalRecordService.UnitTests.Services;

public class ConsultationCompletionServiceTests
{
    [Fact]
    public async Task CompletePreparesDatabaseBeforePublishingStoredEvent()
    {
        var consultationId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var completedEvent = NewEvent(consultationId, doctorId);
        var calls = new List<string>();
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        repository.Setup(item => item.PrepareAsync(consultationId, doctorId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("database"))
            .ReturnsAsync(new CompletionPreparationResult(CompletionPreparationOutcome.Ready, completedEvent));
        var publisher = new Mock<IConsultationCompletedPublisher>(MockBehavior.Strict);
        publisher.Setup(item => item.PublishAsync(completedEvent, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("kafka"))
            .ReturnsAsync(true);

        var result = await CreateService(repository, publisher).CompleteAsync(consultationId, doctorId);

        Assert.Equal(CompleteConsultationOutcome.Success, result.Outcome);
        Assert.Equal(completedEvent.EventId, result.EventId);
        Assert.Equal(new[] { "database", "kafka" }, calls);
        repository.VerifyAll();
        publisher.VerifyAll();
    }

    [Fact]
    public async Task MissingVitalSignsDoesNotPublish()
    {
        var consultationId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        repository.Setup(item => item.PrepareAsync(consultationId, doctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompletionPreparationResult(CompletionPreparationOutcome.VitalSignsMissing));
        var publisher = new Mock<IConsultationCompletedPublisher>(MockBehavior.Strict);

        var result = await CreateService(repository, publisher).CompleteAsync(consultationId, doctorId);

        Assert.Equal(CompleteConsultationOutcome.VitalSignsMissing, result.Outcome);
        Assert.Null(result.EventId);
        repository.VerifyAll();
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FailedPublishCanBeRetriedWithTheSameStoredEventId()
    {
        var consultationId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var completedEvent = NewEvent(consultationId, doctorId);
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        repository.Setup(item => item.PrepareAsync(consultationId, doctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompletionPreparationResult(CompletionPreparationOutcome.Ready, completedEvent));
        var publishedEvents = new List<ConsultationCompletedEvent>();
        var publisher = new Mock<IConsultationCompletedPublisher>(MockBehavior.Strict);
        var attempt = 0;
        publisher.Setup(item => item.PublishAsync(It.IsAny<ConsultationCompletedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsultationCompletedEvent message, CancellationToken _) =>
            {
                publishedEvents.Add(message);
                return ++attempt == 2;
            });

        var service = CreateService(repository, publisher);
        var failed = await service.CompleteAsync(consultationId, doctorId);
        var retried = await service.CompleteAsync(consultationId, doctorId);

        Assert.Equal(CompleteConsultationOutcome.PublishFailed, failed.Outcome);
        Assert.Equal(CompleteConsultationOutcome.Success, retried.Outcome);
        Assert.Equal(completedEvent.EventId, retried.EventId);
        Assert.Equal(new[] { completedEvent.EventId, completedEvent.EventId },
            publishedEvents.Select(message => message.EventId));
        repository.Verify(item => item.PrepareAsync(consultationId, doctorId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        publisher.Verify(item => item.PublishAsync(completedEvent, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task PublisherExceptionReturnsRetryableFailure()
    {
        var consultationId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var completedEvent = NewEvent(consultationId, doctorId);
        var repository = new Mock<IConsultationCompletionRepository>();
        repository.Setup(item => item.PrepareAsync(consultationId, doctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompletionPreparationResult(CompletionPreparationOutcome.Ready, completedEvent));
        var publisher = new Mock<IConsultationCompletedPublisher>();
        publisher.Setup(item => item.PublishAsync(completedEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));

        var result = await CreateService(repository, publisher).CompleteAsync(consultationId, doctorId);

        Assert.Equal(CompleteConsultationOutcome.PublishFailed, result.Outcome);
    }

    private static ConsultationCompletedEvent NewEvent(Guid consultationId, Guid doctorId) =>
        new(Guid.NewGuid(), consultationId, Guid.NewGuid(), Guid.NewGuid(), doctorId);

    private static ConsultationCompletionService CreateService(
        Mock<IConsultationCompletionRepository> repository,
        Mock<IConsultationCompletedPublisher> publisher) =>
        new(repository.Object, publisher.Object, NullLogger<ConsultationCompletionService>.Instance);
}
