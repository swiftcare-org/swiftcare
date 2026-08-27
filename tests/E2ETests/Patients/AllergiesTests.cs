using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Patients;

// Covers SWC-17 (Manage Allergies). This story is unusually UI-heavy - the red
// alert banner, the table, the severe-first ordering are only observable in a
// browser - so the coverage stays close to the acceptance criteria. The
// "allergy name is required" message is proven at the unit/API level and is
// only spot-checked here.
[Trait("Category", "E2E")]
public class AllergiesTests : SeleniumTestBase
{
    // Scenario 1 - successful addition: appears in the list immediately and
    // raises the red alert.
    [Fact]
    public void AddAllergy_AppearsInListAndRaisesRedAlert()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        Assert.False(profile.HasAllergyAlert);

        profile.AddAllergy("Penicillin", "Severe", "Anaphylaxis");

        profile.WaitForAllergyRow("Penicillin");
        profile.WaitForAllergyAlertContaining("Penicillin");
    }

    // Scenario 2 - missing allergy name blocks the save.
    [Fact]
    public void AddAllergy_WithNoName_ShowsRequiredError()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.SubmitAddAllergyForm();

        Assert.Equal("Allergy name is required", profile.AddAllergyNameError);
        Assert.False(profile.HasAllergyAlert);
    }

    // Scenario 3 - view: allergies are listed severe-first.
    [Fact]
    public void Allergies_AreListedSevereFirst()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();
        seed.AddAllergy(patient.PatientId, "Dust Mites", "Mild");
        seed.AddAllergy(patient.PatientId, "Penicillin", "Severe");

        AppSession.LogIn(Driver, "dr.chen");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.WaitForAllergyRow("Penicillin");

        var severities = profile.AllergySeveritiesInOrder.Select(s => s.ToUpperInvariant()).ToList();
        Assert.Equal(new[] { "SEVERE", "MILD" }, severities);
        Assert.Equal("Penicillin", profile.AllergyNamesInOrder[0]);
    }

    // Scenario 4 - update: the list and the red alert both reflect the edit.
    [Fact]
    public void EditAllergy_UpdatesListAndAlert()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();
        seed.AddAllergy(patient.PatientId, "Peanuts", "Severe");

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.WaitForAllergyRow("Peanuts");

        profile.StartEdit("Peanuts");
        profile.SubmitEdit("Tree Nuts");

        profile.WaitForAllergyRow("Tree Nuts");
        profile.WaitForAllergyRowGone("Peanuts");
        profile.WaitForAllergyAlertContaining("Tree Nuts");
    }

    // Scenario 5 - remove the last allergy: after confirming, the red alert is gone.
    [Fact]
    public void RemovingLastAllergy_ClearsRedAlert()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();
        seed.AddAllergy(patient.PatientId, "Latex", "Moderate");

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.WaitForAllergyAlertContaining("Latex");

        profile.StartRemove("Latex");
        profile.ConfirmRemove();

        profile.WaitForNoAllergyAlert();
        Assert.True(profile.ShowsNoAllergiesRecorded);
    }
}
