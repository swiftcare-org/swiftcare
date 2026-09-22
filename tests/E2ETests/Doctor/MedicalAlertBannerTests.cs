using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Doctor;

// Browser coverage for SWC-28's combined clinical alert stack. The test data is
// unique to each case. The overdue fixture is first created and completed through
// the real APIs, then only that test-owned row is backdated because SWC-122 correctly
// prevents doctors from entering a past follow-up date.
[Trait("Category", "E2E")]
[Collection(E2ETestCollections.SharedQueue)]
public class MedicalAlertBannerTests : SeleniumTestBase
{
    [Fact]
    public void PatientWithAllAlertTypes_ShowsOneBannerPerAlertInRedAmberBlueOrder()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.AddAllergy(patient.PatientId, "Penicillin", "Severe");
        seed.AddAllergy(patient.PatientId, "Latex", "Mild");
        seed.AddChronicCondition(patient.PatientId, "Type 2 Diabetes", "2022-01-15");

        seed.WaitUntilWaiting(patient.PatientId);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);
        seed.CallNext(doctor.Username, doctor.Password);
        var assignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        Assert.Equal(patient.PatientId, assignment.PatientId);

        const string followUpInstructions = "Review blood pressure";
        var consultation = seed.CreateConsultationWithFollowUp(
            assignment,
            doctor.Username,
            doctor.Password,
            ClinicClock.Today,
            followUpInstructions);
        seed.RecordVitalSigns(consultation.Id, doctor.Username, doctor.Password);
        seed.CompleteConsultation(consultation.Id, doctor.Username, doctor.Password);
        MedicalRecordDatabase.BackdateCompletedFollowUp(
            consultation.Id,
            patient.PatientId,
            ClinicClock.Today.AddDays(-1));

        AppSession.LogIn(Driver, doctor.Username, doctor.Password);
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.WaitForMedicalAlertCount(4);

        Assert.Equal(
            new[] { "Allergy Alert", "Allergy Alert", "Chronic Condition Alert", "Follow-up Alert" },
            profile.MedicalAlertLabelsInOrder);

        var messages = profile.MedicalAlertMessagesInOrder;
        Assert.Contains(messages, message => message.Contains("ALLERGY: Penicillin — Severe"));
        Assert.Contains(messages, message => message.Contains("ALLERGY: Latex — Mild"));
        Assert.Equal("⚠️ CONDITION: Type 2 Diabetes (since Jan 2022)", messages[2]);
        Assert.Equal($"📌 FOLLOW-UP: {followUpInstructions} — overdue", messages[3]);

        var classes = profile.MedicalAlertClassNamesInOrder;
        Assert.Contains("border-red-700", classes[0]);
        Assert.Contains("border-red-700", classes[1]);
        Assert.Contains("border-amber-600", classes[2]);
        Assert.Contains("border-blue-700", classes[3]);
    }

    [Fact]
    public void PatientWithoutClinicalAlerts_ShowsNoAlertBanners()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatientOutsideQueue();

        AppSession.LogIn(Driver, doctor.Username, doctor.Password);
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.WaitForNoMedicalAlerts();

        Assert.Equal(0, profile.MedicalAlertCount);
        Assert.True(profile.ShowsNoAllergiesRecorded);
        Assert.True(profile.ShowsNoChronicConditionsRecorded);
    }
}
