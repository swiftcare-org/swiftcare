using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MedicalRecordService.Data;
using MedicalRecordService.Models.Configuration;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;

namespace MedicalRecordService.UnitTests.Services;

// Argument checks and unexpected-outcome handling of the MedicalRecordService services.
// Strict repository mocks prove that a rejected call never reaches the database
// (SWC-151 mutation testing).
public class ServiceGuardClauseTests
{
    private static readonly Guid SomeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task LatestCompletedConsultationNeedsADoctorId()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CompletionService().FindLatestCompletedAsync(Guid.Empty));

        AssertArgument(exception, "doctorId", "Doctor ID must be provided.");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ProgressForQueueNeedsBothIds(bool emptyQueueId, bool emptyDoctorId)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CompletionService().FindByQueueAsync(
                emptyQueueId ? Guid.Empty : SomeId,
                emptyDoctorId ? Guid.Empty : SomeId));

        Assert.Equal("Queue and doctor IDs must be provided.", exception.Message);
    }

    [Fact]
    public async Task ProgressForQueueWithBothIdsAsksTheRepository()
    {
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.FindByQueueAsync(SomeId, SomeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsultationProgressResponse?)null);

        Assert.Null(await CompletionService(repository).FindByQueueAsync(SomeId, SomeId));
        repository.VerifyAll();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CompletionNeedsBothIds(bool emptyConsultationId, bool emptyDoctorId)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CompletionService().CompleteAsync(
                emptyConsultationId ? Guid.Empty : SomeId,
                emptyDoctorId ? Guid.Empty : SomeId));

        Assert.Equal("Consultation and doctor IDs must be provided.", exception.Message);
    }

    [Fact]
    public async Task CompletingAnUnknownConsultationReportsNotFound()
    {
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.PrepareAsync(SomeId, SomeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompletionPreparationResult(CompletionPreparationOutcome.ConsultationNotFound));

        var result = await CompletionService(repository).CompleteAsync(SomeId, SomeId);

        Assert.Equal(CompleteConsultationOutcome.ConsultationNotFound, result.Outcome);
        Assert.Null(result.EventId);
    }

    [Fact]
    public async Task ReadyConsultationWithoutAnEventIsAnInvariantViolation()
    {
        var repository = new Mock<IConsultationCompletionRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.PrepareAsync(SomeId, SomeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompletionPreparationResult(CompletionPreparationOutcome.Ready));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CompletionService(repository).CompleteAsync(SomeId, SomeId));

        Assert.Equal("A ready consultation has no completion event.", exception.Message);
    }

    [Fact]
    public async Task CreatingAConsultationNeedsADoctorId()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            ConsultationService().CreateAsync(ValidConsultation(), Guid.Empty, "Dr. Amara Chen", "R-204"));

        AssertArgument(exception, "doctorId", "Doctor ID must be provided.");
    }

    [Fact]
    public async Task CreatingAConsultationNeedsARequest()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ConsultationService().CreateAsync(null!, SomeId, "Dr. Amara Chen", "R-204"));
    }

    [Theory]
    [InlineData("   ", "R-204", "doctorName")]
    [InlineData("Dr. Amara Chen", "   ", "roomNumber")]
    public async Task CreatingAConsultationNeedsDoctorNameAndRoom(string doctorName, string roomNumber, string parameter)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            ConsultationService().CreateAsync(ValidConsultation(), SomeId, doctorName, roomNumber));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public async Task CreatingAConsultationRejectsAnUnknownPersistenceOutcome()
    {
        var repository = new Mock<IConsultationRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.CreateAsync(It.IsAny<ConsultationDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultationPersistenceResult { Outcome = (ConsultationPersistenceOutcome)99 });

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ConsultationService(repository).CreateAsync(ValidConsultation(), SomeId, "Dr. Amara Chen", "R-204"));

        Assert.StartsWith("Unsupported consultation persistence outcome.", exception.Message);
    }

    [Theory]
    [InlineData(true, false, "consultationId", "Consultation ID must be provided.")]
    [InlineData(false, true, "doctorId", "Doctor ID must be provided.")]
    public async Task RecordingVitalSignsNeedsBothIds(
        bool emptyConsultationId,
        bool emptyDoctorId,
        string parameter,
        string message)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            VitalSignsService().RecordAsync(
                emptyConsultationId ? Guid.Empty : SomeId,
                emptyDoctorId ? Guid.Empty : SomeId,
                new RecordVitalSignsRequest { PulseRate = 72 }));

        AssertArgument(exception, parameter, message);
    }

    [Fact]
    public async Task RecordingVitalSignsNeedsARequest()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            VitalSignsService().RecordAsync(SomeId, SomeId, null!));
    }

    [Fact]
    public async Task RecordingVitalSignsRejectsAnUnknownPersistenceOutcome()
    {
        var repository = new Mock<IVitalSignsRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.CreateAsync(It.IsAny<VitalSignsDraft>(), SomeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VitalSignsPersistenceOutcome)99);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            VitalSignsService(repository).RecordAsync(SomeId, SomeId, new RecordVitalSignsRequest { PulseRate = 72 }));

        Assert.StartsWith("Unsupported vital-signs persistence outcome.", exception.Message);
    }

    [Fact]
    public async Task PatientLookupsNeedAPatientId()
    {
        var followUp = await Assert.ThrowsAsync<ArgumentException>(() =>
            new ConsultationFollowUpService(
                    Mock.Of<IConsultationFollowUpRepository>(MockBehavior.Strict),
                    Options.Create(new MedicalRecordOptions { TimeZone = "Asia/Colombo" }),
                    TimeProvider.System)
                .FindOverdueAsync(Guid.Empty));
        var history = await Assert.ThrowsAsync<ArgumentException>(() =>
            new ConsultationHistoryService(Mock.Of<IConsultationHistoryRepository>(MockBehavior.Strict))
                .GetHistoryAsync(Guid.Empty));
        var vitals = await Assert.ThrowsAsync<ArgumentException>(() =>
            new VitalSignsHistoryService(Mock.Of<IVitalSignsHistoryRepository>(MockBehavior.Strict))
                .GetHistoryAsync(Guid.Empty));

        AssertArgument(followUp, "patientId", "Patient ID must be provided.");
        AssertArgument(history, "patientId", "Patient ID must be provided.");
        AssertArgument(vitals, "patientId", "Patient ID must be provided.");
    }

    private static void AssertArgument(ArgumentException exception, string parameter, string message)
    {
        Assert.Equal(parameter, exception.ParamName);
        Assert.StartsWith(message, exception.Message);
    }

    private static ConsultationCompletionService CompletionService(
        Mock<IConsultationCompletionRepository>? repository = null) => new(
            (repository ?? new Mock<IConsultationCompletionRepository>(MockBehavior.Strict)).Object,
            Mock.Of<IConsultationCompletedPublisher>(MockBehavior.Strict),
            NullLogger<ConsultationCompletionService>.Instance);

    private static ConsultationService ConsultationService(Mock<IConsultationRepository>? repository = null) => new(
        (repository ?? new Mock<IConsultationRepository>(MockBehavior.Strict)).Object,
        TimeProvider.System,
        Options.Create(new MedicalRecordOptions { TimeZone = "Asia/Colombo" }));

    private static VitalSignsService VitalSignsService(Mock<IVitalSignsRepository>? repository = null) => new(
        (repository ?? new Mock<IVitalSignsRepository>(MockBehavior.Strict)).Object,
        TimeProvider.System);

    private static CreateConsultationRequest ValidConsultation() => new()
    {
        QueueId = SomeId,
        PatientId = SomeId,
        Symptoms = "Headache",
        Diagnosis = "Tension headache"
    };
}
