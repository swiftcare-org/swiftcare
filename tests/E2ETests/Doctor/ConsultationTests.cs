using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Doctor;

// Covers the three SWC-24 (Create Consultation Record) acceptance-criterion scenarios
// that are frontend rendering decisions and cannot be proven through Postman: live
// template pre-fill and free editing in the browser, and client-side required-field
// validation (ConsultationPage.tsx sets noValidate and checks symptoms/diagnosis itself
// before ever calling the API). Server-side behaviour - doctor identity from the JWT,
// consultationDate, the duplicate-queue conflict, and the exact validation error bodies
// - is covered by docs/testing/postman/SWC-24-collection.json.
//
// Each test creates its own throwaway Doctor account via SeedClient rather than reusing
// the seeded dr.chen: there is no complete-consultation endpoint yet, so once an account
// calls next it stays occupied for the rest of the clinic day (the same constraint the
// SWC-22 Postman collection documents), and a shared account would make this suite
// unsafe to re-run.
[Trait("Category", "E2E")]
public class ConsultationTests : SeleniumTestBase
{
    // Known, stable seed fixture from services/MedicalRecordService/Database/schema.sql
    // (INSERT IGNORE on a unique Name, so it never changes across runs).
    private const string RespiratorySymptomsTemplate = "Respiratory symptoms:\n- ";
    private const string RespiratoryFindingsTemplate = "Respiratory examination findings:\n- ";

    // Scenario A (AC1) - a doctor fills every field by hand, with no template, and saves.
    [Fact]
    public void CreateConsultation_WithAllFieldsAndNoTemplate_ShowsSavedConfirmation()
    {
        var consultation = ArrangeDoctorWithCurrentPatient();

        consultation.FillSymptoms("QA E2E symptoms - fever and dry cough for two days");
        consultation.FillExaminationFindings("QA E2E findings - clear chest, no wheeze");
        consultation.FillDiagnosis("QA E2E diagnosis - viral upper respiratory infection");
        consultation.FillNotes("QA E2E notes - review if symptoms persist beyond 5 days");
        consultation.ClickSave();

        consultation.WaitForSavedConfirmation();
        Assert.True(consultation.HasSavedConfirmation());
    }

    // Scenario B (AC2) - selecting a template pre-fills symptoms/findings/notes, the
    // doctor can still edit that text, and the edited form saves successfully. This is
    // the one behaviour the Postman collection cannot see: it proves the DOM was
    // actually pre-filled and remained editable, not just that the API accepts edited
    // text.
    [Fact]
    public void SelectTemplate_PreFillsFieldsAndRemainsEditable_ThenSaves()
    {
        var consultation = ArrangeDoctorWithCurrentPatient();

        consultation.SelectTemplate("Respiratory Consultation");

        Assert.Equal(RespiratorySymptomsTemplate, consultation.SymptomsValue);
        Assert.Equal(RespiratoryFindingsTemplate, consultation.ExaminationFindingsValue);

        consultation.AppendToSymptoms("wheeze noted on the left side, QA E2E edited");
        consultation.AppendToExaminationFindings("reduced air entry left base, QA E2E edited");
        consultation.AppendToNotes("review in 3 days, QA E2E edited");
        consultation.FillDiagnosis("QA E2E diagnosis - suspected mild asthma exacerbation");

        // The template text is still there, proving the field was appended to, not
        // cleared and retyped - "the doctor can edit the pre-filled text freely", not
        // that they must replace it.
        Assert.StartsWith(RespiratorySymptomsTemplate, consultation.SymptomsValue);
        Assert.Contains("QA E2E edited", consultation.SymptomsValue);

        consultation.ClickSave();

        consultation.WaitForSavedConfirmation();
        Assert.True(consultation.HasSavedConfirmation());
    }

    // Scenario C (AC3) - submitting with symptoms and diagnosis both blank shows
    // inline validation errors and never reaches the "saved" state. This exercises
    // ConsultationPage.tsx's own client-side check (the form has noValidate and never
    // calls the API for this case), a separate code path from the server's 400.
    [Fact]
    public void Submit_WithSymptomsAndDiagnosisBlank_ShowsValidationErrorsAndDoesNotSave()
    {
        var consultation = ArrangeDoctorWithCurrentPatient();

        consultation.ClickSave();

        Assert.Equal("Symptoms are required", consultation.SymptomsFieldError);
        Assert.Equal("Diagnosis is required", consultation.DiagnosisFieldError);
        Assert.False(consultation.HasSavedConfirmation());
    }

    // Shared arrangement for all three scenarios: a fresh throwaway doctor calls next
    // on a freshly registered, checked-in patient through the real dashboard UI (not
    // seeded over HTTP - the "current patient" the consultation form reads is
    // sessionStorage state written by DoctorDashboard's own call-next click handler,
    // see frontend/src/consultations/currentPatientStorage.ts), then opens the
    // consultation form via the dashboard's "Record Consultation" link.
    private ConsultationPage ArrangeDoctorWithCurrentPatient()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        var login = new LoginPage(Driver);
        login.NavigateTo();
        login.SubmitCredentials(doctor.Username, doctor.Password);
        login.WaitForRedirectAwayFromLogin();

        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        dashboard.ClickCallNextPatient();
        dashboard.WaitForCurrentPatientPanel();
        dashboard.ClickRecordConsultation();

        var consultation = new ConsultationPage(Driver);
        consultation.WaitUntilLoaded();
        return consultation;
    }
}
