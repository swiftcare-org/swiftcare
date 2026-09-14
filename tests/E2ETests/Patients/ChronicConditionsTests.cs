using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Patients;

// Covers SWC-18 (Manage Chronic Conditions). Like SWC-17's allergies, this story
// is UI-heavy: the amber alert banner, the immediate list update, and the
// removal confirmation prompt are only observable in a browser. Deep
// validation-message plumbing (missing name, the exact future-date message,
// the clinic-timezone boundary) is already proven at the unit level
// (ChronicConditionsControllerTests, ClinicDateProviderTests) and at the API
// level (docs/testing/postman/SWC-18-collection.json); it is only
// spot-checked here, matching the pattern in AllergiesTests.
[Trait("Category", "E2E")]
public class ChronicConditionsTests : SeleniumTestBase
{
    // Journey 1 - add: a receptionist adds a condition and sees it immediately,
    // with no page reload; a doctor viewing the same patient sees the amber
    // alert. Covers AC1, AC4, AC2.
    [Fact]
    public void AddCondition_AppearsImmediatelyAndRaisesDoctorAlert()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var receptionistView = new PatientProfilePage(Driver);
        receptionistView.WaitUntilLoaded();
        Assert.True(receptionistView.ShowsNoChronicConditionsRecorded);

        receptionistView.AddCondition("Hypertension", "Reviewed quarterly, managed with lifestyle changes.");

        receptionistView.WaitForConditionRow("Hypertension");
        Assert.Contains("Hypertension", receptionistView.ConditionNamesInOrder);

        AppSession.LogIn(Driver, "dr.chen");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var doctorView = new PatientProfilePage(Driver);
        doctorView.WaitUntilLoaded();
        doctorView.WaitForConditionRow("Hypertension");
        doctorView.WaitForChronicConditionAlertContaining("Hypertension");
    }

    // Journey 2 - remove: removing the only condition requires the exact
    // confirmation prompt, then clears both the list and the doctor's amber
    // alert. Covers AC5, AC6, AC7, AC8.
    [Fact]
    public void RemovingLastCondition_RequiresConfirmationAndClearsAlert()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();
        seed.AddChronicCondition(patient.PatientId, "Type 2 Diabetes");

        AppSession.LogIn(Driver, "dr.chen");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var doctorView = new PatientProfilePage(Driver);
        doctorView.WaitUntilLoaded();
        doctorView.WaitForChronicConditionAlertContaining("Type 2 Diabetes");

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var receptionistView = new PatientProfilePage(Driver);
        receptionistView.WaitUntilLoaded();
        receptionistView.WaitForConditionRow("Type 2 Diabetes");

        receptionistView.StartRemove("Type 2 Diabetes");
        // AC6's exact wording. Asserted before confirming, so a missing or
        // reworded prompt fails here rather than being masked by the removal
        // succeeding anyway.
        Assert.True(
            Driver.PageSource.Contains("Are you sure you want to remove this condition?"),
            "expected the exact AC6 confirmation prompt before the condition is removed");

        receptionistView.ConfirmRemove();

        receptionistView.WaitForConditionRowGone("Type 2 Diabetes");
        Assert.True(receptionistView.ShowsNoChronicConditionsRecorded);

        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");
        var doctorViewAfterRemoval = new PatientProfilePage(Driver);
        doctorViewAfterRemoval.WaitUntilLoaded();
        doctorViewAfterRemoval.WaitForNoChronicConditionAlert();
    }

    // Journey 3 - role gating and the future-date guard: a doctor gets a
    // read-only view with no mutation controls, and a receptionist entering a
    // future diagnosed date is blocked client-side with the exact AC3 message
    // before any request is sent. Covers AC3, AC10, and the PR's "hides
    // condition mutation controls from non-receptionist users" claim.
    [Fact]
    public void DoctorCannotMutate_AndFutureDateIsBlockedClientSide()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();
        seed.AddChronicCondition(patient.PatientId, "Asthma");

        AppSession.LogIn(Driver, "dr.chen");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var doctorView = new PatientProfilePage(Driver);
        doctorView.WaitUntilLoaded();
        doctorView.WaitForConditionRow("Asthma");
        Assert.False(doctorView.HasAddConditionForm);
        Assert.False(doctorView.HasConditionRemoveButton("Asthma"));

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var receptionistView = new PatientProfilePage(Driver);
        receptionistView.WaitUntilLoaded();
        Assert.True(receptionistView.HasAddConditionForm);

        var clinicTomorrow = DateOnly.ParseExact(PatientProfilePage.ClinicTodayIsoDate(), "yyyy-MM-dd").AddDays(1);

        receptionistView.EnterConditionName("Future Diagnosis Attempt");
        receptionistView.SetConditionDateDiagnosed(clinicTomorrow.ToString("yyyy-MM-dd"));
        receptionistView.SubmitAddConditionForm();

        Assert.Equal("Diagnosed date cannot be in the future", receptionistView.AddConditionDateError);
        Assert.DoesNotContain("Future Diagnosis Attempt", receptionistView.ConditionNamesInOrder);
    }
}
