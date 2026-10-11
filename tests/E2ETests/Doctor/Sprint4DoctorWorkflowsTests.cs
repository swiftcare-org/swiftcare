using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Doctor;

[Trait("Category", "E2E")]
[Trait("Story", "SWC-135")]
[Collection(E2ETestCollections.SharedQueue)]
public sealed class Sprint4DoctorWorkflowsTests : SeleniumTestBase
{
    [Fact]
    public void Doctor_RecordsFollowUpCompletesConsultationAndRecordsNoPrescription()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient(TestData.FullName("Sprint 4 Follow Up"));
        seed.WaitUntilWaiting(patient.PatientId);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);
        seed.CallNext(doctor.Username, doctor.Password);

        AppSession.LogIn(Driver, doctor.Username, doctor.Password);
        AppSession.GoTo(Driver, "/doctor/consultation");
        var consultation = new ConsultationPage(Driver);
        consultation.WaitUntilLoaded();
        consultation.FillSymptoms("Persistent cough for Sprint 4 verification");
        consultation.FillExaminationFindings("Chest clear on examination");
        consultation.FillDiagnosis("Resolving respiratory infection");
        consultation.FillNotes("Continue hydration and rest");
        consultation.FillFollowUp(ClinicClock.Today.AddDays(7), "Review cough and breathing");
        consultation.ClickSave();
        consultation.WaitForSavedConfirmation();
        consultation.WaitForVitalSignsForm();
        consultation.EnterVital("temperatureCelsius", "37");
        consultation.ClickSaveVitals();
        consultation.WaitForVitalsSavedMessage();
        consultation.WaitUntilCompleteConsultationIsEnabled();
        consultation.ClickCompleteConsultation();

        var prescription = new PrescriptionPage(Driver);
        prescription.WaitUntilReady();
        prescription.RecordNoPrescriptionRequired();
        Assert.Equal("/doctor", new Uri(Driver.Url).AbsolutePath);
        Assert.Equal(
            "No prescription required was recorded.",
            new DoctorDashboardPage(Driver).WaitForArrivalNotice());
    }

    [Fact]
    public void DoctorOwnedTemplate_CanBeUsedByOwnerAndIsHiddenFromAnotherDoctor()
    {
        using var seed = new SeedClient();
        var owner = seed.CreateUser("Doctor");
        var otherDoctor = seed.CreateUser("Doctor");
        var ownerPatient = seed.RegisterPatient(TestData.FullName("Template Owner Patient"));
        seed.WaitUntilWaiting(ownerPatient.PatientId);
        seed.EnsureNextWaitingPatientIs(ownerPatient.PatientId);
        seed.CallNext(owner.Username, owner.Password);

        AppSession.LogIn(Driver, owner.Username, owner.Password);
        AppSession.GoTo(Driver, "/doctor/consultation");
        var consultation = new ConsultationPage(Driver);
        consultation.WaitUntilLoaded();
        consultation.FillSymptoms("Reusable Sprint 4 symptoms");
        consultation.FillExaminationFindings("Reusable Sprint 4 findings");
        consultation.FillNotes("Reusable Sprint 4 notes");
        var templateName = $"Sprint 4 Template {TestData.RunId}";
        consultation.SaveCurrentDraftAsTemplate(templateName);
        consultation.WaitForTemplateOption(templateName);
        consultation.SelectTemplate(templateName);
        Assert.Equal("Reusable Sprint 4 symptoms", consultation.SymptomsValue);
        Assert.Equal("Reusable Sprint 4 findings", consultation.ExaminationFindingsValue);
        Assert.Equal("Reusable Sprint 4 notes", consultation.NotesValue);

        var otherPatient = seed.RegisterPatient(TestData.FullName("Template Privacy Patient"));
        seed.WaitUntilWaiting(otherPatient.PatientId);
        seed.EnsureNextWaitingPatientIs(otherPatient.PatientId);
        seed.CallNext(otherDoctor.Username, otherDoctor.Password);

        AppSession.LogIn(Driver, otherDoctor.Username, otherDoctor.Password);
        AppSession.GoTo(Driver, "/doctor/consultation");
        var otherConsultation = new ConsultationPage(Driver);
        otherConsultation.WaitUntilLoaded();
        otherConsultation.WaitUntilTemplatesLoaded();
        Assert.False(otherConsultation.HasTemplateOption(templateName));
    }
}
