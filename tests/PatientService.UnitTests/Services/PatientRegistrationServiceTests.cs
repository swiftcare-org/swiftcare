using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PatientService.Data;
using PatientService.Models.Dtos;
using PatientService.Models.Entities;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public class PatientRegistrationServiceTests
{
    private const string CorrelationId = "test-correlation-id";
    private static readonly Guid ActingUserId = Guid.NewGuid();

    private static PatientDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PatientDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static PatientRegistrationService CreateService(
        PatientDbContext dbContext,
        Mock<IPatientEventPublisher> publisherMock) =>
        new(dbContext, publisherMock.Object, NullLogger<PatientRegistrationService>.Instance);

    private static RegisterPatientRequest CreateValidRequest(string nic = "199012345678") => new()
    {
        Nic = nic,
        FullName = "Test Patient",
        DateOfBirth = new DateOnly(1990, 4, 17),
        Gender = Gender.Male,
        Address = "123 Test Road, Colombo",
        PhoneNumber = "0771234567",
        BloodGroup = BloodGroup.APositive
    };

    [Fact]
    public async Task RegisterWithValidRequestPersistsPatientAndPublishesExactlyOneNewPatientEvent()
    {
        await using var dbContext = CreateDbContext();
        var publisherMock = new Mock<IPatientEventPublisher>();
        publisherMock
            .Setup(p => p.PublishPatientCheckedInAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService(dbContext, publisherMock);

        var result = await service.RegisterPatientAsync(CreateValidRequest(), CorrelationId, ActingUserId);

        Assert.Equal(RegisterPatientOutcome.Success, result.Outcome);
        Assert.NotNull(result.Patient);
        var persisted = Assert.Single(dbContext.Patients);
        Assert.Equal(result.Patient!.PatientId, persisted.Id);

        publisherMock.Verify(
            p => p.PublishPatientCheckedInAsync(persisted.Id, true, CorrelationId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RegisterWithExistingNicReturnsDuplicateNicAndPersistsNothing()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Patients.Add(new Patient
        {
            Nic = "199012345678",
            FullName = "Existing Patient",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Female,
            Address = "Existing Address",
            PhoneNumber = "0771111111",
            BloodGroup = BloodGroup.ONegative
        });
        await dbContext.SaveChangesAsync();

        var publisherMock = new Mock<IPatientEventPublisher>();
        var service = CreateService(dbContext, publisherMock);

        var result = await service.RegisterPatientAsync(CreateValidRequest(), CorrelationId, ActingUserId);

        Assert.Equal(RegisterPatientOutcome.DuplicateNic, result.Outcome);
        Assert.Null(result.Patient);
        Assert.Single(dbContext.Patients);
        publisherMock.Verify(
            p => p.PublishPatientCheckedInAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterWithNicMatchingSoftDeletedPatientReturnsDuplicateNic()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Patients.Add(new Patient
        {
            Nic = "199012345678",
            FullName = "Former Patient",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Male,
            Address = "Former Address",
            PhoneNumber = "0772222222",
            BloodGroup = BloodGroup.BPositive,
            IsDeleted = true
        });
        await dbContext.SaveChangesAsync();

        var publisherMock = new Mock<IPatientEventPublisher>();
        var service = CreateService(dbContext, publisherMock);

        var result = await service.RegisterPatientAsync(CreateValidRequest(), CorrelationId, ActingUserId);

        Assert.Equal(RegisterPatientOutcome.DuplicateNic, result.Outcome);
    }

    [Theory]
    [InlineData("199012345678", "199012345678")]
    [InlineData(" 199012345678 ", "199012345678")]
    [InlineData("199012345678v", "199012345678V")]
    public async Task RegisterNormalizesNicCaseAndWhitespaceBeforeCollisionCheck(string existingNic, string submittedNic)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Patients.Add(new Patient
        {
            Nic = existingNic.Trim().ToUpperInvariant(),
            FullName = "Existing Patient",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Female,
            Address = "Existing Address",
            PhoneNumber = "0771111111",
            BloodGroup = BloodGroup.ONegative
        });
        await dbContext.SaveChangesAsync();

        var publisherMock = new Mock<IPatientEventPublisher>();
        var service = CreateService(dbContext, publisherMock);

        var result = await service.RegisterPatientAsync(CreateValidRequest(submittedNic), CorrelationId, ActingUserId);

        Assert.Equal(RegisterPatientOutcome.DuplicateNic, result.Outcome);
    }

    [Fact]
    public async Task RegisterWhenPublishFailsStillReturnsSuccess()
    {
        await using var dbContext = CreateDbContext();
        var publisherMock = new Mock<IPatientEventPublisher>();
        publisherMock
            .Setup(p => p.PublishPatientCheckedInAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(dbContext, publisherMock);

        var result = await service.RegisterPatientAsync(CreateValidRequest(), CorrelationId, ActingUserId);

        Assert.Equal(RegisterPatientOutcome.Success, result.Outcome);
        Assert.Single(dbContext.Patients);
    }
}
