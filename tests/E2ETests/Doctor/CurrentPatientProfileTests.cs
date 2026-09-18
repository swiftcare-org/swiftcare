using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Doctor;

// Covers the browser-only behaviour introduced by SWC-92: the two identifiers in a
// Doctor's current-consultation panel link to the assigned patient's existing profile,
// the profile keeps the Doctor's established read-only demographic access, and no links
// are rendered when that Doctor has no current-patient assignment.
[Trait("Category", "E2E")]
public class CurrentPatientProfileTests : SeleniumTestBase
{
    [Fact]
    public void CurrentPatientLinks_OpenTheSameDoctorReadablePatientProfile()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.AddAllergy(patient.PatientId, "Penicillin", "Severe", "SWC-110 Selenium coverage");
        seed.AddChronicCondition(patient.PatientId, "Asthma", notes: "SWC-110 Selenium coverage");

        var dashboard = OpenDashboardAs(doctor);
        Browser.StoreCurrentPatientAssignment(Driver, patient);
        dashboard.WaitUntilLoaded();
        dashboard.WaitForCurrentPatientPanel();

        var queueNumber = dashboard.CurrentPatientQueueNumberLinkText;
        var patientName = dashboard.CurrentPatientNameLinkText;
        var queueNumberTarget = dashboard.CurrentPatientQueueNumberProfilePath;
        var patientNameTarget = dashboard.CurrentPatientNameProfilePath;

        Assert.Matches(@"^Q-\d+$", queueNumber);
        Assert.False(string.IsNullOrWhiteSpace(patientName));
        Assert.Equal(queueNumberTarget, patientNameTarget);

        dashboard.ClickCurrentPatientQueueNumber();
        var profile = AssertDoctorReadableProfile(queueNumberTarget);
        var openedPatientId = profile.PatientId;

        Driver.Navigate().Back();
        dashboard.WaitUntilLoaded();
        dashboard.WaitForCurrentPatientPanel();
        Assert.Equal(patientNameTarget, dashboard.CurrentPatientNameProfilePath);

        dashboard.ClickCurrentPatientName();
        profile = AssertDoctorReadableProfile(patientNameTarget);
        Assert.Equal(openedPatientId, profile.PatientId);
    }

    [Fact]
    public void DoctorWithoutCurrentPatient_ShowsNoPatientProfileLinks()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");

        var dashboard = OpenDashboardAs(doctor);

        Assert.False(dashboard.HasCurrentPatientPanel);
        Assert.Equal(0, dashboard.CurrentPatientProfileLinkCount);
    }

    private PatientProfilePage AssertDoctorReadableProfile(string expectedPath)
    {
        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();

        Assert.Equal(expectedPath, new Uri(Driver.Url).AbsolutePath);
        var expectedPatientId = expectedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        Assert.True(
            string.Equals(expectedPatientId, profile.PatientId, StringComparison.OrdinalIgnoreCase),
            $"Expected profile for patient '{expectedPatientId}', but page showed '{profile.PatientId}'.");
        Assert.False(string.IsNullOrWhiteSpace(profile.PatientId));
        Assert.False(string.IsNullOrWhiteSpace(profile.Nic));
        Assert.False(string.IsNullOrWhiteSpace(profile.PhoneNumber));
        Assert.False(string.IsNullOrWhiteSpace(profile.BloodGroup));
        Assert.False(string.IsNullOrWhiteSpace(profile.Address));
        Assert.True(profile.HasAllergiesSection);
        Assert.True(profile.HasChronicConditionsSection);
        profile.WaitForAllergyRow("Penicillin");
        profile.WaitForConditionRow("Asthma");
        Assert.False(profile.HasEditProfileButton);
        Assert.False(profile.HasProfileEditForm);

        return profile;
    }

    private DoctorDashboardPage OpenDashboardAs(SeededUser doctor)
    {
        var login = new LoginPage(Driver);
        login.NavigateTo();
        login.SubmitCredentials(doctor.Username, doctor.Password);
        login.WaitForRedirectAwayFromLogin();

        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        return dashboard;
    }
}
