using Microsoft.EntityFrameworkCore;
using PatientService.Data;
using PatientService.Models.Entities;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public class PatientProfileServiceTests
{
    private static PatientDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PatientDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Patient CreatePatient(bool isDeleted = false) => new()
    {
        Nic = "199012345678",
        FullName = "Test Patient",
        DateOfBirth = new DateOnly(1990, 4, 17),
        Gender = Gender.Male,
        Address = "123 Test Road, Colombo",
        PhoneNumber = "0771234567",
        BloodGroup = BloodGroup.APositive,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task GetPatientReturnsProfileForExistingPatient()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = new PatientProfileService(dbContext);

        var profile = await service.GetPatientAsync(patient.Id);

        Assert.NotNull(profile);
        Assert.Equal(patient.Id, profile!.PatientId);
        Assert.Equal(patient.FullName, profile.FullName);
        Assert.Equal(patient.Nic, profile.Nic);
        Assert.Equal(patient.PhoneNumber, profile.PhoneNumber);
        Assert.Equal(patient.BloodGroup, profile.BloodGroup);
    }

    [Fact]
    public async Task GetPatientForUnknownIdReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var service = new PatientProfileService(dbContext);

        var profile = await service.GetPatientAsync(Guid.NewGuid());

        Assert.Null(profile);
    }

    [Fact]
    public async Task GetPatientForSoftDeletedPatientReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient(isDeleted: true);
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = new PatientProfileService(dbContext);

        var profile = await service.GetPatientAsync(patient.Id);

        Assert.Null(profile);
    }
}
