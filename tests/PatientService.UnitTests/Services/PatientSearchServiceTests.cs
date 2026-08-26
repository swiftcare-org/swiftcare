using Microsoft.EntityFrameworkCore;
using PatientService.Data;
using PatientService.Models.Entities;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public class PatientSearchServiceTests
{
    private static PatientDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<PatientDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Patient CreatePatient(
        string nic,
        string fullName,
        string phoneNumber,
        bool isDeleted = false) => new()
    {
        Nic = nic,
        FullName = fullName,
        DateOfBirth = new DateOnly(1990, 4, 17),
        Gender = Gender.Male,
        Address = "123 Test Road, Colombo",
        PhoneNumber = phoneNumber,
        BloodGroup = BloodGroup.APositive,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task SearchByPartialNameReturnsMatchingPatient()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient("199012345678", "Kasun Perera", "0771234567");
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync("Perer");

        Assert.Single(results);
        Assert.Equal(patient.Id, results[0].PatientId);
    }

    [Theory]
    [InlineData("kasun")]
    [InlineData("KASUN")]
    public async Task SearchByNameIsCaseInsensitiveInBothDirections(string term)
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient("199012345678", "Kasun Perera", "0771234567");
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync(term);

        Assert.Single(results);
        Assert.Equal(patient.Id, results[0].PatientId);
    }

    [Fact]
    public async Task SearchByPartialNicReturnsMatchingPatient()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient("199012345678", "Kasun Perera", "0771234567");
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync("901234");

        Assert.Single(results);
        Assert.Equal(patient.Id, results[0].PatientId);
    }

    [Fact]
    public async Task SearchByLowercaseNicSuffixMatchesStoredUppercaseNic()
    {
        await using var dbContext = CreateDbContext();
        // Nic is normalized to uppercase by PatientRegistrationService before it is ever
        // persisted, so the stored value always ends in 'V', never 'v'.
        var patient = CreatePatient("199012345V", "Kasun Perera", "0771234567");
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync("2345v");

        Assert.Single(results);
        Assert.Equal(patient.Id, results[0].PatientId);
    }

    [Fact]
    public async Task SearchByPartialPhoneNumberReturnsMatchingPatient()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient("199012345678", "Kasun Perera", "0771234567");
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync("1234567");

        Assert.Single(results);
        Assert.Equal(patient.Id, results[0].PatientId);
    }

    [Fact]
    public async Task SearchWithNoMatchesReturnsEmptyList()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Patients.Add(CreatePatient("199012345678", "Kasun Perera", "0771234567"));
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync("nobody-matches-this");

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public async Task SearchWithTermShorterThanTwoCharactersReturnsEmptyList(string? term)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Patients.Add(CreatePatient("199012345678", "Kasun Perera", "0771234567"));
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync(term);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchExcludesSoftDeletedPatients()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Patients.Add(CreatePatient("199012345678", "Kasun Perera", "0771234567", isDeleted: true));
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync("Kasun");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchReturnsAtMostTwentyResultsOrderedByFullName()
    {
        await using var dbContext = CreateDbContext();
        for (var i = 0; i < 25; i++)
        {
            dbContext.Patients.Add(CreatePatient(
                $"19901234{i:D4}",
                $"Search Patient {i:D2}",
                $"07712{i:D5}"));
        }
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync("Search Patient");

        Assert.Equal(20, results.Count);
        Assert.Equal(results.OrderBy(r => r.FullName).Select(r => r.PatientId), results.Select(r => r.PatientId));
    }

    [Fact]
    public async Task SearchResultContainsOnlyThePermittedFields()
    {
        await using var dbContext = CreateDbContext();
        var patient = CreatePatient("199012345678", "Kasun Perera", "0771234567");
        dbContext.Patients.Add(patient);
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync("Kasun");

        var result = Assert.Single(results);
        Assert.Equal(patient.Id, result.PatientId);
        Assert.Equal(patient.FullName, result.FullName);
        Assert.Equal(patient.Nic, result.Nic);
        Assert.Equal(patient.PhoneNumber, result.PhoneNumber);
        Assert.Equal(patient.BloodGroup, result.BloodGroup);
    }

    [Fact]
    public async Task SearchTermContainingLikeWildcardsIsMatchedLiterally()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Patients.Add(CreatePatient("199012345678", "Kasun Perera", "0771234567"));
        await dbContext.SaveChangesAsync();
        var service = new PatientSearchService(dbContext);

        var results = await service.SearchPatientsAsync("%_%");

        Assert.Empty(results);
    }
}
