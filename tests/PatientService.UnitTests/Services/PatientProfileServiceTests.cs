using Microsoft.EntityFrameworkCore;
using PatientService.Data;
using PatientService.Models.Entities;
using PatientService.Models.Dtos;
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

    [Fact]
    public async Task UpdatePatientPersistsAddressPhoneNumberAndBloodGroup()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = new PatientProfileService(dbContext);

        var result = await service.UpdatePatientAsync(patient.Id, new UpdatePatientRequest
        {
            Address = " 456 Updated Road, Colombo ",
            PhoneNumber = " 0777654321 ",
            BloodGroup = BloodGroup.ONegative
        });

        Assert.NotNull(result);
        Assert.Equal("456 Updated Road, Colombo", result!.Address);
        Assert.Equal("0777654321", result.PhoneNumber);
        Assert.Equal(BloodGroup.ONegative, result.BloodGroup);

        var persisted = await dbContext.Patients.SingleAsync(p => p.Id == patient.Id);
        Assert.Equal("456 Updated Road, Colombo", persisted.Address);
        Assert.Equal("0777654321", persisted.PhoneNumber);
        Assert.Equal(BloodGroup.ONegative, persisted.BloodGroup);
    }

    [Fact]
    public async Task UpdatePatientDoesNotModifyProtectedRegistrationFields()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var originalNic = patient.Nic;
        var originalDateOfBirth = patient.DateOfBirth;
        var originalFullName = patient.FullName;
        var originalGender = patient.Gender;
        var service = new PatientProfileService(dbContext);

        await service.UpdatePatientAsync(patient.Id, new UpdatePatientRequest
        {
            Address = "456 Updated Road, Colombo",
            PhoneNumber = "0777654321",
            BloodGroup = BloodGroup.ABNegative
        });

        var persisted = await dbContext.Patients.SingleAsync(p => p.Id == patient.Id);
        Assert.Equal(originalNic, persisted.Nic);
        Assert.Equal(originalDateOfBirth, persisted.DateOfBirth);
        Assert.Equal(originalFullName, persisted.FullName);
        Assert.Equal(originalGender, persisted.Gender);
    }

    [Fact]
    public async Task UpdatePatientForUnknownIdReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var service = new PatientProfileService(dbContext);

        var result = await service.UpdatePatientAsync(Guid.NewGuid(), new UpdatePatientRequest
        {
            Address = "456 Updated Road, Colombo",
            PhoneNumber = "0777654321",
            BloodGroup = BloodGroup.ONegative
        });

        Assert.Null(result);
        Assert.Empty(dbContext.Patients);
    }

    [Fact]
    public async Task UpdateSoftDeletedPatientReturnsNullWithoutMutation()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient(isDeleted: true);
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var originalAddress = patient.Address;
        var service = new PatientProfileService(dbContext);

        var result = await service.UpdatePatientAsync(patient.Id, new UpdatePatientRequest
        {
            Address = "456 Updated Road, Colombo",
            PhoneNumber = "0777654321",
            BloodGroup = BloodGroup.ONegative
        });

        Assert.Null(result);
        Assert.Equal(originalAddress, patient.Address);
    }
}
