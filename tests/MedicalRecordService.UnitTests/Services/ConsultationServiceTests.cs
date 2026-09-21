using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Models.Configuration;
using MedicalRecordService.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace MedicalRecordService.UnitTests.Services;

public class ConsultationServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 13, 8, 45, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtcNow;
    }

    [Fact]
    public async Task CreatePersistsPatientQueueDoctorAndEditableConsultationFields()
    {
        var repository = new Mock<IConsultationRepository>();
        ConsultationDraft? persisted = null;
        repository
            .Setup(repo => repo.CreateAsync(
                It.IsAny<ConsultationDraft>(),
                It.IsAny<CancellationToken>()))
            .Callback<ConsultationDraft, CancellationToken>((consultation, _) =>
                persisted = consultation)
            .ReturnsAsync(new ConsultationPersistenceResult
            {
                Outcome = ConsultationPersistenceOutcome.Success,
                TemplateName = "General Consultation"
            });
        var service = CreateService(repository);
        var patientId = Guid.NewGuid();
        var queueId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var result = await service.CreateAsync(
            new CreateConsultationRequest
            {
                PatientId = patientId,
                QueueId = queueId,
                Symptoms = "  Persistent cough  ",
                ExaminationFindings = "  Clear breath sounds  ",
                Diagnosis = "  Upper respiratory infection  ",
                Notes = "  Encourage fluids  ",
                TemplateId = templateId
            },
            doctorId,
            "  Dr. Amara Chen  ",
            "  R-204  ");

        Assert.Equal(CreateConsultationOutcome.Success, result.Outcome);
        Assert.NotNull(persisted);
        Assert.Equal(patientId, persisted.PatientId);
        Assert.Equal(queueId, persisted.QueueId);
        Assert.Equal(doctorId, persisted.DoctorId);
        Assert.Equal("Dr. Amara Chen", persisted.DoctorName);
        Assert.Equal("R-204", persisted.RoomNumber);
        Assert.Equal("Persistent cough", persisted.Symptoms);
        Assert.Equal("Clear breath sounds", persisted.ExaminationFindings);
        Assert.Equal("Upper respiratory infection", persisted.Diagnosis);
        Assert.Equal("Encourage fluids", persisted.Notes);
        Assert.Equal(templateId, persisted.TemplateId);
        Assert.Equal(FixedUtcNow.UtcDateTime, persisted.ConsultationDate);
        Assert.Equal(DateTimeKind.Utc, persisted.ConsultationDate.Kind);

        var response = Assert.IsType<ConsultationResponse>(result.Consultation);
        Assert.Equal(persisted.Id, response.Id);
        Assert.Equal(patientId, response.PatientId);
        Assert.Equal(queueId, response.QueueId);
        Assert.Equal(doctorId, response.DoctorId);
        Assert.Equal("Dr. Amara Chen", response.DoctorName);
        Assert.Equal("R-204", response.RoomNumber);
        Assert.Equal("General Consultation", response.TemplateName);
        Assert.Equal(FixedUtcNow.UtcDateTime, response.ConsultationDate);
    }

    [Fact]
    public async Task CreateWithUnavailableTemplateReturnsTemplateNotFound()
    {
        var repository = new Mock<IConsultationRepository>();
        repository
            .Setup(repo => repo.CreateAsync(
                It.IsAny<ConsultationDraft>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultationPersistenceResult
            {
                Outcome = ConsultationPersistenceOutcome.TemplateNotFound
            });
        var service = CreateService(repository);

        var result = await service.CreateAsync(
            ValidRequest(templateId: Guid.NewGuid()),
            Guid.NewGuid(),
            "Dr. Amara Chen",
            "R-204");

        Assert.Equal(CreateConsultationOutcome.TemplateNotFound, result.Outcome);
        Assert.Null(result.Consultation);
    }

    [Fact]
    public async Task CreateWithPastFollowUpDateReturnsValidationOutcomeWithoutPersisting()
    {
        var repository = new Mock<IConsultationRepository>(MockBehavior.Strict);
        var request = ValidRequest(
            followUpDate: new DateOnly(2026, 9, 12),
            followUpInstructions: "Review blood pressure");

        var result = await CreateService(repository).CreateAsync(
            request,
            Guid.NewGuid(),
            "Dr. Amara Chen",
            "R-204");

        Assert.Equal(CreateConsultationOutcome.FollowUpDateInPast, result.Outcome);
        Assert.Null(result.Consultation);
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    public async Task CreateAcceptsTodayOrFutureFollowUpDate(int day)
    {
        var repository = new Mock<IConsultationRepository>();
        ConsultationDraft? persisted = null;
        repository
            .Setup(repo => repo.CreateAsync(
                It.IsAny<ConsultationDraft>(),
                It.IsAny<CancellationToken>()))
            .Callback<ConsultationDraft, CancellationToken>((consultation, _) =>
                persisted = consultation)
            .ReturnsAsync(new ConsultationPersistenceResult
            {
                Outcome = ConsultationPersistenceOutcome.Success
            });
        var followUpDate = new DateOnly(2026, 9, day);
        var request = ValidRequest(
            followUpDate: followUpDate,
            followUpInstructions: "Review blood pressure");

        var result = await CreateService(repository).CreateAsync(
            request,
            Guid.NewGuid(),
            "Dr. Amara Chen",
            "R-204");

        Assert.Equal(CreateConsultationOutcome.Success, result.Outcome);
        Assert.Equal(followUpDate, persisted?.FollowUpDate);
        Assert.Equal("Review blood pressure", persisted?.FollowUpInstructions);
    }

    private static CreateConsultationRequest ValidRequest(
        Guid? templateId = null,
        DateOnly? followUpDate = null,
        string? followUpInstructions = null) => new()
        {
            PatientId = Guid.NewGuid(),
            QueueId = Guid.NewGuid(),
            Symptoms = "Persistent cough",
            Diagnosis = "Upper respiratory infection",
            FollowUpDate = followUpDate,
            FollowUpInstructions = followUpInstructions,
            TemplateId = templateId
        };

    private static ConsultationService CreateService(Mock<IConsultationRepository> repository) =>
        new(
            repository.Object,
            new FixedTimeProvider(),
            Options.Create(new MedicalRecordOptions { TimeZone = "Asia/Colombo" }));
}
