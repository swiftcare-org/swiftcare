using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Patients;

// Browser coverage for SWC-94 (isolate allergy-call failure). Only the browser's request for
// this test patient's allergies is made to fail, through BrowserFaults; the backend and every
// other request are untouched. The patient is registered outside today's queue, so these
// tests do not need the shared-queue collection.
[Trait("Category", "E2E")]
public class AllergyLoadFailureTests : SeleniumTestBase
{
    // The profile stays open and usable: only the allergies section reports the failure,
    // while demographics, chronic conditions and the receptionist's check-in remain available.
    [Fact]
    public void Receptionist_ProfileStaysUsable_WhenOnlyTheAllergiesRequestFails()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatientOutsideQueue();
        seed.AddChronicCondition(patient.PatientId, "Hypertension", "2023-03-01");

        BrowserFaults.FailRequests(Driver, $"/api/patients/{patient.PatientId}/allergies");
        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.WaitForAllergiesLoadError();

        Assert.False(profile.HasProfileLoadError);
        Assert.False(profile.ShowsNoAllergiesRecorded);
        Assert.Equal(patient.PatientId, profile.PatientId, ignoreCase: true);
        Assert.Equal(patient.Nic, profile.Nic);
        Assert.Equal(patient.PhoneNumber, profile.PhoneNumber);

        profile.WaitForConditionRow("Hypertension");
        Assert.Equal(new[] { "Hypertension" }, profile.ConditionNamesInOrder);
        Assert.False(profile.ShowsChronicConditionsLoadError);

        profile.WaitForCheckInButton();
        Assert.True(profile.HasCheckInButton);
    }

    // A Doctor sees medical alert banners built from allergies (SWC-28). With the allergies
    // request failing, the profile still opens and the chronic-condition alert is still shown.
    [Fact]
    public void Doctor_ProfileStillShowsConditionAlert_WhenOnlyTheAllergiesRequestFails()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatientOutsideQueue();
        seed.AddChronicCondition(patient.PatientId, "Asthma", "2021-06-10");

        BrowserFaults.FailRequests(Driver, $"/api/patients/{patient.PatientId}/allergies");
        AppSession.LogIn(Driver, "dr.chen");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.WaitForAllergiesLoadError();

        Assert.False(profile.HasProfileLoadError);
        Assert.Equal(patient.PatientId, profile.PatientId, ignoreCase: true);
        profile.WaitForChronicConditionAlertContaining("Asthma");
        profile.WaitForConditionRow("Asthma");
        Assert.False(profile.HasAllergyAlert);
    }
}
