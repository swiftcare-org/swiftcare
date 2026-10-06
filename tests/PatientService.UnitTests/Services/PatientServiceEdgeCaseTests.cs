using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PatientService.Data;
using PatientService.Models.Dtos;
using PatientService.Models.Entities;
using PatientService.Models.Enums;
using PatientService.Models.Validation;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

// Persistence, soft-deletion, ordering and boundary cases of the PatientService services and
// validation attributes. Changes are read back through a second DbContext, because the
// InMemory provider would otherwise return the tracked entity even if SaveChanges never ran
// (SWC-151 mutation testing).
public class PatientServiceEdgeCaseTests
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    [Fact]
    public async Task UpdatedAllergyIsSavedToTheDatabase()
    {
        var (patient, allergy) = await SeedPatientWithAllergyAsync();
        await using (var dbContext = CreateDbContext())
        {
            await new AllergyService(dbContext, NullLogger<AllergyService>.Instance).UpdateAllergyAsync(
                patient.Id,
                allergy.Id,
                new AllergyRequest { AllergyName = "Latex", Severity = AllergySeverity.Mild },
                Guid.NewGuid());
        }

        await using var verify = CreateDbContext();
        Assert.Equal("Latex", (await verify.Allergies.SingleAsync()).AllergyName);
    }

    [Fact]
    public async Task RemovedAllergyIsSoftDeletedInTheDatabase()
    {
        var (patient, allergy) = await SeedPatientWithAllergyAsync();
        await using (var dbContext = CreateDbContext())
        {
            await new AllergyService(dbContext, NullLogger<AllergyService>.Instance)
                .RemoveAllergyAsync(patient.Id, allergy.Id, Guid.NewGuid());
        }

        await using var verify = CreateDbContext();
        Assert.True((await verify.Allergies.SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task UpdatedPatientProfileIsSavedToTheDatabase()
    {
        var patient = await SeedPatientAsync();
        await using (var dbContext = CreateDbContext())
        {
            await new PatientProfileService(dbContext).UpdatePatientAsync(
                patient.Id,
                new UpdatePatientRequest
                {
                    Address = "45 Galle Road, Colombo",
                    PhoneNumber = "0779876543",
                    BloodGroup = BloodGroup.ONegative
                });
        }

        await using var verify = CreateDbContext();
        var stored = await verify.Patients.SingleAsync();
        Assert.Equal("45 Galle Road, Colombo", stored.Address);
        Assert.Equal("0779876543", stored.PhoneNumber);
    }

    [Fact]
    public async Task ConditionsOfAnUnknownPatientAreNotFound()
    {
        await SeedPatientAsync();
        await using var dbContext = CreateDbContext();

        Assert.Null(await ConditionService(dbContext).GetConditionsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ConditionsOfASoftDeletedPatientAreNotFound()
    {
        var patient = await SeedPatientAsync(isDeleted: true);
        await using var dbContext = CreateDbContext();

        Assert.Null(await ConditionService(dbContext).GetConditionsAsync(patient.Id));
    }

    [Fact]
    public async Task ConditionsDiagnosedOnTheSameDayAreListedAlphabetically()
    {
        var patient = await SeedPatientAsync();
        var diagnosed = new DateOnly(2024, 3, 1);
        await using (var seed = CreateDbContext())
        {
            seed.ChronicConditions.AddRange(
                new ChronicCondition { PatientId = patient.Id, ConditionName = "Hypertension", DateDiagnosed = diagnosed },
                new ChronicCondition { PatientId = patient.Id, ConditionName = "Asthma", DateDiagnosed = diagnosed });
            await seed.SaveChangesAsync();
        }

        await using var dbContext = CreateDbContext();
        var conditions = await ConditionService(dbContext).GetConditionsAsync(patient.Id);

        Assert.Equal(["Asthma", "Hypertension"], conditions!.Select(condition => condition.ConditionName));
    }

    [Fact]
    public async Task BlankConditionNotesAreStoredAsNoNotes()
    {
        var patient = await SeedPatientAsync();
        await using var dbContext = CreateDbContext();

        var condition = await ConditionService(dbContext).AddConditionAsync(
            patient.Id,
            new ChronicConditionRequest { ConditionName = "Asthma", DateDiagnosed = new DateOnly(2024, 3, 1), Notes = "   " },
            Guid.NewGuid());

        Assert.Null(condition!.Notes);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(128)]
    public async Task SearchAcceptsTermsAtTheLengthLimits(int length)
    {
        var name = new string('a', length);
        await SeedPatientAsync(fullName: name);
        await using var dbContext = CreateDbContext();

        var results = await new PatientSearchService(dbContext).SearchPatientsAsync(name);

        Assert.Single(results);
    }

    [Fact]
    public async Task PatientsWithTheSameNameAreOrderedById()
    {
        var lower = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higher = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await SeedPatientAsync(id: higher, nic: "199012345679");
        await SeedPatientAsync(id: lower, nic: "199012345678");
        await using var dbContext = CreateDbContext();

        var results = await new PatientSearchService(dbContext).SearchPatientsAsync("Test Patient");

        Assert.Equal([lower, higher], results.Select(result => result.PatientId));
    }

    [Fact]
    public void DateOfBirthCheckLeavesMissingValuesToTheRequiredAttribute()
    {
        Assert.True(new PastDateAttribute().IsValid(null));
    }

    [Fact]
    public void DateOfBirthExactlyAtTheMaximumAgeIsAccepted()
    {
        var oldest = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-130);

        Assert.True(new PastDateAttribute().IsValid(oldest));
    }

    [Fact]
    public void ClinicDateValidationWithoutAClinicDateProviderFailsClearly()
    {
        var attribute = new NotFutureClinicDateAttribute { ErrorMessage = "Date cannot be in the future" };
        var context = new ValidationContext(new object());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            attribute.GetValidationResult(new DateOnly(2024, 3, 1), context));

        Assert.Equal("IClinicDateProvider is required for clinic-date validation.", exception.Message);
    }

    [Fact]
    public void NewRequestsFailRequiredTextFieldsInsteadOfUsingAPlaceholder()
    {
        Assert.Contains("Condition name is required", Messages(new ChronicConditionRequest()));
        Assert.Contains("NIC is required.", Messages(new RegisterPatientRequest()));
        Assert.Contains("Phone number is required.", Messages(new RegisterPatientRequest()));
        Assert.Contains("Phone number is required.", Messages(new UpdatePatientRequest()));
    }

    private static List<string?> Messages(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results.Select(result => result.ErrorMessage).ToList();
    }

    private PatientDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PatientDbContext>().UseInMemoryDatabase(_databaseName).Options);

    private static ChronicConditionService ConditionService(PatientDbContext dbContext) =>
        new(dbContext, NullLogger<ChronicConditionService>.Instance);

    private async Task<Patient> SeedPatientAsync(
        bool isDeleted = false,
        string fullName = "Test Patient",
        Guid? id = null,
        string nic = "199012345678")
    {
        var patient = new Patient
        {
            Id = id ?? Guid.NewGuid(),
            Nic = nic,
            FullName = fullName,
            DateOfBirth = new DateOnly(1990, 4, 17),
            Gender = Gender.Male,
            Address = "123 Test Road, Colombo",
            PhoneNumber = "0771234567",
            BloodGroup = BloodGroup.APositive,
            IsDeleted = isDeleted
        };
        await using var dbContext = CreateDbContext();
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        return patient;
    }

    private async Task<(Patient Patient, Allergy Allergy)> SeedPatientWithAllergyAsync()
    {
        var patient = await SeedPatientAsync();
        var allergy = new Allergy { PatientId = patient.Id, AllergyName = "Penicillin", Severity = AllergySeverity.Severe };
        await using var dbContext = CreateDbContext();
        dbContext.Allergies.Add(allergy);
        await dbContext.SaveChangesAsync();
        return (patient, allergy);
    }
}
