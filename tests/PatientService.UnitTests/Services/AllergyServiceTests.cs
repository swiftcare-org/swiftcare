using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PatientService.Data;
using PatientService.Models.Dtos;
using PatientService.Models.Entities;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public class AllergyServiceTests
{
    private static readonly Guid ActingUserId = Guid.NewGuid();

    private static PatientDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PatientDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AllergyService CreateService(PatientDbContext dbContext) =>
        new(dbContext, NullLogger<AllergyService>.Instance);

    private static Patient CreatePatient(string nic = "199012345678", bool isDeleted = false) => new()
    {
        Nic = nic,
        FullName = "Test Patient",
        DateOfBirth = new DateOnly(1990, 4, 17),
        Gender = Gender.Male,
        Address = "123 Test Road, Colombo",
        PhoneNumber = "0771234567",
        BloodGroup = BloodGroup.APositive,
        IsDeleted = isDeleted
    };

    private static Allergy CreateAllergy(
        Guid patientId,
        string name = "Penicillin",
        AllergySeverity severity = AllergySeverity.Severe,
        string? notes = null,
        bool isDeleted = false) => new()
        {
            PatientId = patientId,
            AllergyName = name,
            Severity = severity,
            Notes = notes,
            IsDeleted = isDeleted
        };

    private static AllergyRequest ValidRequest(
        string name = "Penicillin",
        AllergySeverity severity = AllergySeverity.Severe,
        string? notes = "Causes rash") => new()
        {
            AllergyName = name,
            Severity = severity,
            Notes = notes
        };

    [Fact]
    public async Task AddAllergyWithValidRequestPersistsAndReturnsResponse()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.AddAllergyAsync(patient.Id, ValidRequest(), ActingUserId);

        Assert.NotNull(response);
        Assert.Equal("Penicillin", response!.AllergyName);
        Assert.Equal(AllergySeverity.Severe, response.Severity);
        Assert.Equal("Causes rash", response.Notes);
        Assert.Single(dbContext.Allergies);
    }

    [Fact]
    public async Task AddAllergyTrimsNameAndNotes()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.AddAllergyAsync(
            patient.Id, ValidRequest(name: "  Penicillin  ", notes: "  Causes rash  "), ActingUserId);

        Assert.Equal("Penicillin", response!.AllergyName);
        Assert.Equal("Causes rash", response.Notes);
    }

    [Fact]
    public async Task AddAllergyWithBlankNotesStoresNull()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.AddAllergyAsync(patient.Id, ValidRequest(notes: "   "), ActingUserId);

        Assert.Null(response!.Notes);
    }

    [Fact]
    public async Task AddAllergyForUnknownPatientReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var response = await service.AddAllergyAsync(Guid.NewGuid(), ValidRequest(), ActingUserId);

        Assert.Null(response);
        Assert.Empty(dbContext.Allergies);
    }

    [Fact]
    public async Task AddAllergyForSoftDeletedPatientReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient(isDeleted: true);
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.AddAllergyAsync(patient.Id, ValidRequest(), ActingUserId);

        Assert.Null(response);
    }

    [Fact]
    public async Task GetAllergiesOrdersSevereBeforeModerateBeforeMild()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        dbContext.Allergies.AddRange(
            CreateAllergy(patient.Id, name: "Dust", severity: AllergySeverity.Mild),
            CreateAllergy(patient.Id, name: "Penicillin", severity: AllergySeverity.Severe),
            CreateAllergy(patient.Id, name: "Pollen", severity: AllergySeverity.Moderate));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var results = await service.GetAllergiesAsync(patient.Id);

        Assert.NotNull(results);
        Assert.Equal(["Penicillin", "Pollen", "Dust"], results!.Select(a => a.AllergyName));
    }

    [Fact]
    public async Task GetAllergiesOrdersSameSeverityNewestFirst()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        var older = CreateAllergy(patient.Id, name: "Older", severity: AllergySeverity.Severe);
        var newer = CreateAllergy(patient.Id, name: "Newer", severity: AllergySeverity.Severe);
        dbContext.Allergies.AddRange(older, newer);
        await dbContext.SaveChangesAsync();
        older.CreatedAt = DateTime.UtcNow.AddDays(-1);
        newer.CreatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var results = await service.GetAllergiesAsync(patient.Id);

        Assert.Equal(["Newer", "Older"], results!.Select(a => a.AllergyName));
    }

    [Fact]
    public async Task GetAllergiesForPatientWithNoneReturnsEmptyList()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var results = await service.GetAllergiesAsync(patient.Id);

        Assert.NotNull(results);
        Assert.Empty(results!);
    }

    [Fact]
    public async Task GetAllergiesExcludesSoftDeletedAllergies()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        dbContext.Allergies.Add(CreateAllergy(patient.Id, isDeleted: true));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var results = await service.GetAllergiesAsync(patient.Id);

        Assert.Empty(results!);
    }

    [Fact]
    public async Task GetAllergiesForUnknownPatientReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var results = await service.GetAllergiesAsync(Guid.NewGuid());

        Assert.Null(results);
    }

    [Fact]
    public async Task GetAllergiesDoesNotReturnAnotherPatientsAllergies()
    {
        await using var dbContext = CreateDbContext();
        var patientA = CreatePatient(nic: "199012345678");
        var patientB = CreatePatient(nic: "199087654321");
        dbContext.Patients.AddRange(patientA, patientB);
        dbContext.Allergies.Add(CreateAllergy(patientB.Id, name: "Shellfish"));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var results = await service.GetAllergiesAsync(patientA.Id);

        Assert.Empty(results!);
    }

    [Theory]
    [InlineData(AllergySeverity.Severe)]
    [InlineData(AllergySeverity.Moderate)]
    [InlineData(AllergySeverity.Mild)]
    public async Task UpdateAllergyPersistsSeverity(AllergySeverity newSeverity)
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        var allergy = CreateAllergy(patient.Id, severity: AllergySeverity.Mild);
        dbContext.Allergies.Add(allergy);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.UpdateAllergyAsync(
            patient.Id, allergy.Id, ValidRequest(severity: newSeverity), ActingUserId);

        Assert.NotNull(response);
        Assert.Equal(newSeverity, response!.Severity);
    }

    [Fact]
    public async Task UpdateAllergyPersistsNameAndNotes()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        var allergy = CreateAllergy(patient.Id, name: "Old Name", notes: "Old notes");
        dbContext.Allergies.Add(allergy);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.UpdateAllergyAsync(
            patient.Id, allergy.Id, ValidRequest(name: "New Name", notes: "New notes"), ActingUserId);

        Assert.Equal("New Name", response!.AllergyName);
        Assert.Equal("New notes", response.Notes);
    }

    [Fact]
    public async Task UpdateAllergyBelongingToAnotherPatientReturnsNullAndMutatesNothing()
    {
        await using var dbContext = CreateDbContext();
        var patientA = CreatePatient(nic: "199012345678");
        var patientB = CreatePatient(nic: "199087654321");
        dbContext.Patients.AddRange(patientA, patientB);
        var allergy = CreateAllergy(patientB.Id, name: "Original");
        dbContext.Allergies.Add(allergy);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.UpdateAllergyAsync(
            patientA.Id, allergy.Id, ValidRequest(name: "Hijacked"), ActingUserId);

        Assert.Null(response);
        var unchanged = await dbContext.Allergies.FindAsync(allergy.Id);
        Assert.Equal("Original", unchanged!.AllergyName);
    }

    [Fact]
    public async Task UpdateSoftDeletedAllergyReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        var allergy = CreateAllergy(patient.Id, isDeleted: true);
        dbContext.Allergies.Add(allergy);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.UpdateAllergyAsync(patient.Id, allergy.Id, ValidRequest(), ActingUserId);

        Assert.Null(response);
    }

    [Fact]
    public async Task RemoveAllergySetsIsDeletedAndKeepsTheRow()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        var allergy = CreateAllergy(patient.Id);
        dbContext.Allergies.Add(allergy);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var removed = await service.RemoveAllergyAsync(patient.Id, allergy.Id, ActingUserId);

        Assert.True(removed);
        var stored = await dbContext.Allergies.FindAsync(allergy.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsDeleted);
    }

    [Fact]
    public async Task RemoveAllergyBelongingToAnotherPatientReturnsFalseAndMutatesNothing()
    {
        await using var dbContext = CreateDbContext();
        var patientA = CreatePatient(nic: "199012345678");
        var patientB = CreatePatient(nic: "199087654321");
        dbContext.Patients.AddRange(patientA, patientB);
        var allergy = CreateAllergy(patientB.Id);
        dbContext.Allergies.Add(allergy);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var removed = await service.RemoveAllergyAsync(patientA.Id, allergy.Id, ActingUserId);

        Assert.False(removed);
        var unchanged = await dbContext.Allergies.FindAsync(allergy.Id);
        Assert.False(unchanged!.IsDeleted);
    }

    [Fact]
    public async Task RemoveAlreadyRemovedAllergyReturnsFalse()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        var allergy = CreateAllergy(patient.Id, isDeleted: true);
        dbContext.Allergies.Add(allergy);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var removed = await service.RemoveAllergyAsync(patient.Id, allergy.Id, ActingUserId);

        Assert.False(removed);
    }

    [Fact]
    public async Task RemoveAllergyForUnknownAllergyIdReturnsFalse()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var removed = await service.RemoveAllergyAsync(patient.Id, Guid.NewGuid(), ActingUserId);

        Assert.False(removed);
    }
}
