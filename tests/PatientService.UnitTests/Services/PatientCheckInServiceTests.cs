using Microsoft.EntityFrameworkCore;
using Moq;
using PatientService.Data;
using PatientService.Models.Entities;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public class PatientCheckInServiceTests
{
    private const string CorrelationId = "check-in-correlation-id";

    private static PatientDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PatientDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Patient NewPatient(bool isDeleted = false) => new()
    {
        Nic = "199012345678",
        FullName = "Returning Patient",
        DateOfBirth = new DateOnly(1990, 4, 17),
        Gender = Gender.Male,
        Address = "123 Test Road, Colombo",
        PhoneNumber = "0771234567",
        BloodGroup = BloodGroup.APositive,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task CheckInExistingPatientPublishesReturningPatientEvent()
    {
        await using var dbContext = CreateDbContext();
        var patient = NewPatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();

        var publisherMock = new Mock<IPatientEventPublisher>();
        publisherMock
            .Setup(publisher => publisher.PublishPatientCheckedInAsync(
                patient.Id,
                false,
                CorrelationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new PatientCheckInService(dbContext, publisherMock.Object);

        var outcome = await service.CheckInPatientAsync(patient.Id, CorrelationId);

        Assert.Equal(CheckInPatientOutcome.Success, outcome);
        publisherMock.Verify(
            publisher => publisher.PublishPatientCheckedInAsync(
                patient.Id,
                false,
                CorrelationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckInUnknownPatientReturnsNotFoundWithoutPublishing()
    {
        await using var dbContext = CreateDbContext();
        var publisherMock = new Mock<IPatientEventPublisher>();
        var service = new PatientCheckInService(dbContext, publisherMock.Object);

        var outcome = await service.CheckInPatientAsync(Guid.NewGuid(), CorrelationId);

        Assert.Equal(CheckInPatientOutcome.PatientNotFound, outcome);
        publisherMock.Verify(
            publisher => publisher.PublishPatientCheckedInAsync(
                It.IsAny<Guid>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckInSoftDeletedPatientReturnsNotFoundWithoutPublishing()
    {
        await using var dbContext = CreateDbContext();
        var patient = NewPatient(isDeleted: true);
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();

        var publisherMock = new Mock<IPatientEventPublisher>();
        var service = new PatientCheckInService(dbContext, publisherMock.Object);

        var outcome = await service.CheckInPatientAsync(patient.Id, CorrelationId);

        Assert.Equal(CheckInPatientOutcome.PatientNotFound, outcome);
        publisherMock.Verify(
            publisher => publisher.PublishPatientCheckedInAsync(
                It.IsAny<Guid>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckInWhenEventPublishFailsReturnsPublishFailure()
    {
        await using var dbContext = CreateDbContext();
        var patient = NewPatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();

        var publisherMock = new Mock<IPatientEventPublisher>();
        publisherMock
            .Setup(publisher => publisher.PublishPatientCheckedInAsync(
                patient.Id,
                false,
                CorrelationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new PatientCheckInService(dbContext, publisherMock.Object);

        var outcome = await service.CheckInPatientAsync(patient.Id, CorrelationId);

        Assert.Equal(CheckInPatientOutcome.EventPublishFailed, outcome);
    }
}
