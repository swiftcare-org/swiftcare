using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PatientService.Data;
using PatientService.Models.Dtos;
using PatientService.Models.Entities;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public class ChronicConditionServiceTests
{
    private static readonly Guid ActingUserId = Guid.NewGuid();

    private static PatientDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PatientDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ChronicConditionService CreateService(PatientDbContext dbContext) =>
        new(dbContext, NullLogger<ChronicConditionService>.Instance);

    private static Patient NewPatient(bool isDeleted = false) => new()
    {
        Nic = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
        FullName = "Test Patient",
        DateOfBirth = new DateOnly(1990, 4, 17),
        Gender = Gender.Male,
        Address = "123 Test Road, Colombo",
        PhoneNumber = "0771234567",
        BloodGroup = BloodGroup.APositive,
        IsDeleted = isDeleted
    };

    private static ChronicCondition NewCondition(
        Guid patientId,
        string name,
        DateOnly diagnosedDate,
        bool isDeleted = false) => new()
    {
        PatientId = patientId,
        ConditionName = name,
        DateDiagnosed = diagnosedDate,
        Notes = "Ongoing treatment",
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task AddConditionPersistsNameDiagnosedDateAndNotes()
    {
        await using var dbContext = CreateDbContext();
        var patient = NewPatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);
        var diagnosedDate = new DateOnly(2020, 3, 12);

        var response = await service.AddConditionAsync(
            patient.Id,
            new ChronicConditionRequest
            {
                ConditionName = "  Type 2 Diabetes  ",
                DateDiagnosed = diagnosedDate,
                Notes = "  Controlled with medication  "
            },
            ActingUserId);

        Assert.NotNull(response);
        Assert.Equal("Type 2 Diabetes", response.ConditionName);
        Assert.Equal(diagnosedDate, response.DateDiagnosed);
        Assert.Equal("Controlled with medication", response.Notes);
        var stored = Assert.Single(dbContext.ChronicConditions);
        Assert.Equal(patient.Id, stored.PatientId);
        Assert.False(stored.IsDeleted);
    }

    [Fact]
    public async Task AddConditionForUnknownPatientReturnsNullWithoutPersisting()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var response = await service.AddConditionAsync(
            Guid.NewGuid(),
            new ChronicConditionRequest
            {
                ConditionName = "Hypertension",
                DateDiagnosed = new DateOnly(2021, 6, 1)
            },
            ActingUserId);

        Assert.Null(response);
        Assert.Empty(dbContext.ChronicConditions);
    }

    [Fact]
    public async Task GetConditionsReturnsOnlyActiveConditionsForRequestedPatient()
    {
        await using var dbContext = CreateDbContext();
        var patient = NewPatient();
        var otherPatient = NewPatient();
        dbContext.Patients.AddRange(patient, otherPatient);
        dbContext.ChronicConditions.AddRange(
            NewCondition(patient.Id, "Hypertension", new DateOnly(2022, 1, 1)),
            NewCondition(patient.Id, "Asthma", new DateOnly(2019, 2, 1)),
            NewCondition(patient.Id, "Removed", new DateOnly(2023, 1, 1), isDeleted: true),
            NewCondition(otherPatient.Id, "Other patient condition", new DateOnly(2024, 1, 1)));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var conditions = await service.GetConditionsAsync(patient.Id);

        Assert.NotNull(conditions);
        Assert.Equal(["Hypertension", "Asthma"], conditions.Select(condition => condition.ConditionName));
    }

    [Fact]
    public async Task GetConditionsForPatientWithNoneReturnsEmptyList()
    {
        await using var dbContext = CreateDbContext();
        var patient = NewPatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var conditions = await service.GetConditionsAsync(patient.Id);

        Assert.NotNull(conditions);
        Assert.Empty(conditions);
    }

    [Fact]
    public async Task RemoveConditionSoftDeletesRecordAndRemovesItFromActiveList()
    {
        await using var dbContext = CreateDbContext();
        var patient = NewPatient();
        var condition = NewCondition(patient.Id, "Asthma", new DateOnly(2019, 2, 1));
        dbContext.Patients.Add(patient);
        dbContext.ChronicConditions.Add(condition);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var removed = await service.RemoveConditionAsync(patient.Id, condition.Id, ActingUserId);

        Assert.True(removed);
        Assert.True((await dbContext.ChronicConditions.FindAsync(condition.Id))!.IsDeleted);
        Assert.Empty((await service.GetConditionsAsync(patient.Id))!);
    }

    [Fact]
    public async Task RemoveConditionBelongingToAnotherPatientReturnsFalse()
    {
        await using var dbContext = CreateDbContext();
        var patient = NewPatient();
        var otherPatient = NewPatient();
        var condition = NewCondition(otherPatient.Id, "Asthma", new DateOnly(2019, 2, 1));
        dbContext.Patients.AddRange(patient, otherPatient);
        dbContext.ChronicConditions.Add(condition);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var removed = await service.RemoveConditionAsync(patient.Id, condition.Id, ActingUserId);

        Assert.False(removed);
        Assert.False(condition.IsDeleted);
    }
}
