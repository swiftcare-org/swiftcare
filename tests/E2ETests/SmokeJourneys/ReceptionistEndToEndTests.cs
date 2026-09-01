using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.SmokeJourneys;

// Cross-story smoke journey: one receptionist session that spans SWC-9, SWC-12
// and SWC-17 and the navigation between them. This is not extra acceptance-
// criteria coverage - it exists to catch screen-to-screen handoff bugs that the
// per-story tests, which seed their preconditions over the API, cannot see.
[Trait("Category", "E2E")]
[Trait("Category", "Smoke")]
public class ReceptionistEndToEndTests : SeleniumTestBase
{
    [Fact]
    public void Receptionist_RegistersSearchesAndFlagsAnAllergy()
    {
        AppSession.LogIn(Driver, "reception.silva");

        // Register, reached from the dashboard rather than a deep link.
        AppSession.ClickNavLink(Driver, PatientRegistrationPage.Path);

        var registration = new PatientRegistrationPage(Driver);
        registration.WaitUntilLoaded();
        var fullName = TestData.FullName("Journey");
        registration.FillForm(TestData.Nic(), fullName, "1985-03-20", "5 Journey Rd, Kandy", TestData.Phone());
        registration.Submit();
        Assert.Contains("registered successfully", registration.WaitForSuccessMessage());

        // Find the patient just created and open the profile.
        AppSession.GoTo(Driver, PatientSearchPage.Path);
        var search = new PatientSearchPage(Driver);
        search.WaitUntilLoaded();
        search.Search(fullName);
        search.WaitForResultRow(fullName);
        search.OpenResult(fullName);

        // Flag an allergy and confirm the red alert lights up.
        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.AddAllergy("Aspirin", "Severe");
        profile.WaitForAllergyRow("Aspirin");
        profile.WaitForAllergyAlertContaining("Aspirin");
    }
}
