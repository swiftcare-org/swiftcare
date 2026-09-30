using MedicalRecordService.Data;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Services;
using Moq;

namespace MedicalRecordService.UnitTests.Services;

public class ConsultationHistoryServiceTests
{
    [Fact]
    public async Task GetHistoryMapsEveryCompletedConsultationInRepositoryOrder()
    {
        var patientId = Guid.NewGuid();
        var newest = CreateConsultation(patientId, new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc));
        var oldest = CreateConsultation(patientId, new DateTime(2026, 9, 10, 4, 0, 0, DateTimeKind.Utc));
        var repository = new Mock<IConsultationHistoryRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.ListCompletedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([newest, oldest]);

        var result = await new ConsultationHistoryService(repository.Object).GetHistoryAsync(patientId);

        Assert.Equal([newest.Id, oldest.Id], result.Select(item => item.Id));
        var first = result[0];
        Assert.Equal(newest.DoctorName, first.DoctorName);
        Assert.Equal(newest.Symptoms, first.Symptoms);
        Assert.Equal(newest.Diagnosis, first.Diagnosis);
        Assert.Equal(newest.Notes, first.Notes);
        Assert.Equal(newest.ExaminationFindings, first.ExaminationFindings);
        Assert.Equal(newest.FollowUpDate, first.FollowUpDate);
        Assert.Equal(newest.FollowUpInstructions, first.FollowUpInstructions);
        Assert.Equal(newest.ConsultationDate, first.ConsultationDate);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetHistoryReturnsEmptyListForFirstVisit()
    {
        var patientId = Guid.NewGuid();
        var repository = new Mock<IConsultationHistoryRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.ListCompletedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await new ConsultationHistoryService(repository.Object).GetHistoryAsync(patientId);

        Assert.Empty(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetLatestReturnsMostRecentCompletedConsultation()
    {
        var patientId = Guid.NewGuid();
        var latest = CreateConsultation(patientId, new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc));
        var repository = new Mock<IConsultationHistoryRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.FindLatestCompletedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);

        var result = await new ConsultationHistoryService(repository.Object).GetLatestAsync(patientId);

        Assert.NotNull(result);
        Assert.Equal(latest.Id, result.Id);
        Assert.Equal(latest.DoctorName, result.DoctorName);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetLatestReturnsNullWhenPatientHasNoCompletedConsultation()
    {
        var patientId = Guid.NewGuid();
        var repository = new Mock<IConsultationHistoryRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.FindLatestCompletedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Consultation?)null);

        var result = await new ConsultationHistoryService(repository.Object).GetLatestAsync(patientId);

        Assert.Null(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task EmptyPatientIdIsRejectedWithoutQueryingRepository()
    {
        var repository = new Mock<IConsultationHistoryRepository>(MockBehavior.Strict);
        var service = new ConsultationHistoryService(repository.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetHistoryAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetLatestAsync(Guid.Empty));

        repository.VerifyNoOtherCalls();
    }

    private static Consultation CreateConsultation(Guid patientId, DateTime consultationDate) => new()
    {
        Id = Guid.NewGuid(),
        PatientId = patientId,
        QueueId = Guid.NewGuid(),
        DoctorId = Guid.NewGuid(),
        DoctorName = "Dr. Silva",
        RoomNumber = "R-204",
        Symptoms = "Headache",
        ExaminationFindings = "Blood pressure elevated",
        Diagnosis = "Hypertension",
        Notes = "Review in six weeks",
        FollowUpDate = new DateOnly(2026, 11, 2),
        FollowUpInstructions = "Review blood pressure",
        ConsultationDate = consultationDate,
        CreatedAt = consultationDate,
        Status = Consultation.CompleteStatus
    };
}
