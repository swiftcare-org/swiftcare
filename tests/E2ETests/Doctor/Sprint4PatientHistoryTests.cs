using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Doctor;

[Trait("Category", "E2E")]
[Trait("Story", "SWC-135")]
[Collection(E2ETestCollections.SharedQueue)]
public sealed class Sprint4PatientHistoryTests : SeleniumTestBase
{
    [Fact]
    public void Doctor_SeesExpandableConsultationVitalsFollowUpAndPrescriptionHistory()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient(TestData.FullName("Sprint 4 History"));
        seed.WaitUntilWaiting(patient.PatientId);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);
        seed.CallNext(doctor.Username, doctor.Password);
        var assignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        const string followUp = "Review recovery at the next scheduled visit";
        var consultation = seed.CreateConsultationWithFollowUp(
            assignment,
            doctor.Username,
            doctor.Password,
            ClinicClock.Today.AddDays(7),
            followUp);
        seed.RecordVitalSigns(consultation.Id, doctor.Username, doctor.Password);
        seed.CompleteConsultation(consultation.Id, doctor.Username, doctor.Password);
        seed.CreatePrescription(
            consultation,
            doctor.Username,
            doctor.Password,
            new SeededMedicine("Sprint4 Medicine", "10 mg", "Once daily", "5 days", "After food"));

        AppSession.LogIn(Driver, doctor.Username, doctor.Password);
        AppSession.GoTo(Driver, PatientHistoryPage.Path(patient.PatientId));

        var history = new PatientHistoryPage(Driver);
        history.WaitUntilLoaded();
        history.ExpandConsultationContaining(consultation.Diagnosis);

        Assert.True(history.ConsultationDetailsContain(consultation.Diagnosis));
        Assert.True(history.ConsultationDetailsContain(followUp));
        Assert.True(history.VitalsContain("37"));
        Assert.True(history.PrescriptionHistoryContains("Sprint4 Medicine"));
        Assert.True(history.PrescriptionHistoryContains("10 mg"));
    }
}
