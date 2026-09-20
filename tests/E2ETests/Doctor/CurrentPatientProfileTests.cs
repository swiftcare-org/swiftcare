using E2ETests.Drivers;
using E2ETests.Pages;
using E2ETests.Support;
using OpenQA.Selenium;

namespace E2ETests.Doctor;

// Covers SWC-92's current-patient profile links and SWC-112's recovery of the real
// backend assignment in a separate browser session. No test injects a consultation
// into browser storage; queue-dependent cases run exclusively against the shared queue.
[Trait("Category", "E2E")]
[Collection(E2ETestCollections.SharedQueue)]
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
        seed.WaitUntilWaiting(patient.PatientId);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);

        var called = seed.CallNext(doctor.Username, doctor.Password);
        var assignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        Assert.Equal("IN_CONSULTATION", assignment.Status);
        Assert.Equal(Guid.Parse(doctor.UserId), Guid.Parse(assignment.DoctorId));
        Assert.Equal(doctor.FullName, assignment.DoctorName);
        Assert.Equal(patient.PatientId, assignment.PatientId);
        Assert.Equal(called.QueueNumber, assignment.QueueNumber);
        Assert.Equal(doctor.RoomNumber, assignment.RoomNumber);

        var dashboard = OpenDashboardAs(Driver, doctor);
        var panel = dashboard.WaitForCurrentPatientPanel();

        var queueNumber = dashboard.CurrentPatientQueueNumberLinkText;
        var patientName = dashboard.CurrentPatientNameLinkText;
        var queueNumberTarget = dashboard.CurrentPatientQueueNumberProfilePath;
        var patientNameTarget = dashboard.CurrentPatientNameProfilePath;

        Assert.Equal(assignment.QueueNumber, queueNumber);
        Assert.Equal(patient.FullName, patientName);
        Assert.Contains(queueNumber, panel);
        Assert.Contains(patientName, panel);
        Assert.Equal($"Room {assignment.RoomNumber}", dashboard.CurrentRoomText);
        Assert.Equal($"/patients/{patient.PatientId}", queueNumberTarget);
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

        var dashboard = OpenDashboardAs(Driver, doctor);

        dashboard.WaitUntilCurrentPatientStateLoaded();
        Assert.False(dashboard.HasCurrentPatientLoadError);
        Assert.False(dashboard.HasCurrentPatientPanel);
        Assert.Equal(0, dashboard.CurrentPatientProfileLinkCount);
    }

    [Fact]
    public void CurrentPatient_InFreshBrowserSession_IsRestoredFromBackend()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        var originalDashboard = OpenDashboardAs(Driver, doctor);
        originalDashboard.WaitForPatientRow(patient.FullName);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);
        originalDashboard.ClickCallNextPatient();
        var originalPanel = originalDashboard.WaitForCurrentPatientPanel();

        var assignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        Assert.Equal("IN_CONSULTATION", assignment.Status);
        Assert.Equal(Guid.Parse(doctor.UserId), Guid.Parse(assignment.DoctorId));
        Assert.Equal(patient.PatientId, assignment.PatientId);
        Assert.Equal(assignment.QueueNumber, DoctorDashboardPage.QueueNumberFrom(originalPanel));

        // A second ChromeDriver has its own browser profile and no sessionStorage from
        // the tab that called the patient. Log in normally; never inject an assignment.
        var freshDriver = DriverFactory.CreateChromeDriver();
        try
        {
            var login = new LoginPage(freshDriver);
            login.NavigateTo();
            Assert.Null(((IJavaScriptExecutor)freshDriver).ExecuteScript(
                "return sessionStorage.getItem('swiftcare.auth.token');"));
            login.SubmitCredentials(doctor.Username, doctor.Password);
            login.WaitForRedirectAwayFromLogin();

            var freshDashboard = new DoctorDashboardPage(freshDriver);
            freshDashboard.WaitUntilLoaded();
            var restoredPanel = freshDashboard.WaitForCurrentPatientPanel();

            Assert.Equal(assignment.QueueNumber, freshDashboard.CurrentPatientQueueNumberLinkText);
            Assert.Equal(patient.FullName, freshDashboard.CurrentPatientNameLinkText);
            Assert.Equal($"Room {assignment.RoomNumber}", freshDashboard.CurrentRoomText);
            Assert.Equal($"/patients/{assignment.PatientId}", freshDashboard.CurrentPatientNameProfilePath);
            Assert.Equal(DoctorDashboardPage.QueueNumberFrom(originalPanel),
                DoctorDashboardPage.QueueNumberFrom(restoredPanel));
            Assert.False(freshDashboard.IsCallNextEnabled);
        }
        finally
        {
            freshDriver.Quit();
            freshDriver.Dispose();
        }
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

    private static DoctorDashboardPage OpenDashboardAs(IWebDriver driver, SeededUser doctor)
    {
        var login = new LoginPage(driver);
        login.NavigateTo();
        login.SubmitCredentials(doctor.Username, doctor.Password);
        login.WaitForRedirectAwayFromLogin();

        var dashboard = new DoctorDashboardPage(driver);
        dashboard.WaitUntilLoaded();
        return dashboard;
    }
}
