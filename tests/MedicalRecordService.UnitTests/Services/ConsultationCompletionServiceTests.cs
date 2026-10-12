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
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue / 50)]
    public async Task CompletedPageLookupUsesDoctorPageAndCancellationToken(int page)
    {
        var doctor = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<CompletedConsultationContextResponse> visits = [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())];
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        repository.Setup(item => item.FindCompletedPageAsync(doctor, page, cancellation.Token)).ReturnsAsync(visits);
        var publisher = new Mock<IConsultationCompletedPublisher>(MockBehavior.Strict);

        Assert.Same(visits, await CreateService(repository, publisher).FindCompletedPageAsync(doctor, page, cancellation.Token));

        repository.VerifyAll();
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompletedPageWithoutDoctorIsRejected()
    {
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        var publisher = new Mock<IConsultationCompletedPublisher>(MockBehavior.Strict);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => CreateService(repository, publisher).FindCompletedPageAsync(Guid.Empty, 0));
        Assert.Contains("Doctor ID must be provided.", error.Message);
        Assert.Equal("doctorId", error.ParamName);
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue / 50 + 1)]
    [InlineData(int.MaxValue)]
    public async Task CompletedPageOutsideSupportedRangeIsRejected(int page)
    {
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        var publisher = new Mock<IConsultationCompletedPublisher>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateService(repository, publisher).FindCompletedPageAsync(Guid.NewGuid(), page));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LatestCompletedLookupUsesAuthenticatedDoctorId()
    {
        var doctorId = Guid.NewGuid();
        var context = new CompletedConsultationContextResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        repository.Setup(item => item.FindLatestCompletedAsync(
                doctorId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var publisher = new Mock<IConsultationCompletedPublisher>(MockBehavior.Strict);

        var result = await CreateService(repository, publisher)
            .FindLatestCompletedAsync(doctorId);

        Assert.Same(context, result);
        repository.VerifyAll();
        publisher.VerifyNoOtherCalls();
    }

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
        new(Guid.NewGuid(), consultationId, Guid.NewGuid(), Guid.NewGuid(), doctorId, "Viral URTI");

    private static ConsultationCompletionService CreateService(
        Mock<IConsultationCompletionRepository> repository,
        Mock<IConsultationCompletedPublisher> publisher) =>
        new(repository.Object, publisher.Object, NullLogger<ConsultationCompletionService>.Instance);
}
