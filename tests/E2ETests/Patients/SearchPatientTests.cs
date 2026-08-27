using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Patients;

// Covers SWC-12 (Search Patient). The matching logic (partial, case-insensitive,
// across name/NIC/phone) is server-side and covered by PatientService.UnitTests
// and the API suite; these tests confirm the screen calls search and renders
// what comes back, and that the receptionist empty state offers registration.
[Trait("Category", "E2E")]
public class SearchPatientTests : SeleniumTestBase
{
    // Scenario 1 - search by name: a match renders and the row links to the
    // patient's profile.
    [Fact]
    public void SearchByName_ShowsMatchAndOpensProfile()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, PatientSearchPage.Path);

        var search = new PatientSearchPage(Driver);
        search.WaitUntilLoaded();
        search.Search(patient.FullName);
        search.WaitForResultRow(patient.FullName);
        search.OpenResult(patient.FullName);

        Assert.Contains($"/patients/{patient.PatientId}", Driver.Url);

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        Assert.Contains(patient.FullName, Driver.PageSource);
    }

    // Scenario 4 - no results: the receptionist is offered a link to registration.
    [Fact]
    public void SearchWithNoMatches_ShowsEmptyStateWithRegisterLink()
    {
        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, PatientSearchPage.Path);

        var search = new PatientSearchPage(Driver);
        search.WaitUntilLoaded();
        search.Search($"zzz-no-such-patient-{TestData.RunId}");

        Assert.Contains("No patients found", search.WaitForEmptyState());
        Assert.True(search.HasRegisterLink);
        Assert.EndsWith("/reception/patients/new", search.RegisterLinkHref);
    }
}
