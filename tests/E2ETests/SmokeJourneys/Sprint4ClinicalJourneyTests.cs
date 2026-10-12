using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.SmokeJourneys;

[Trait("Category", "E2E")]
[Trait("Category", "Smoke")]
[Trait("Story", "SWC-135")]
[Collection(E2ETestCollections.SharedQueue)]
public sealed class Sprint4ClinicalJourneyTests : SeleniumTestBase
{
    [Fact]
    public void CompletedConsultation_AppearsInActivityFeedAndDailyDiagnosisReport()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient(TestData.FullName("Sprint 4 Journey"));
        seed.WaitUntilWaiting(patient.PatientId);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);

        AppSession.LogIn(Driver, doctor.Username, doctor.Password);
        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        Assert.NotNull(dashboard.WaitForPatientRow(patient.FullName));
        dashboard.ClickCallNextPatient();
        Assert.Contains(patient.FullName, dashboard.WaitForCurrentPatientPanel());
        dashboard.ClickRecordConsultation();

        // Keep the clinical label stable across repeated executions. Daily Report exposes
        // only its top five diagnoses, so run-specific labels would compete as unrelated
        // one-count values and could legitimately fall outside that response on a reused stack.
        const string diagnosis = "Sprint 4 Journey Diagnosis";
        var consultation = new ConsultationPage(Driver);
        consultation.WaitUntilLoaded();
        consultation.FillSymptoms("Sprint 4 end-to-end journey symptoms");
        consultation.FillExaminationFindings("Stable during end-to-end examination");
        consultation.FillDiagnosis(diagnosis);
        consultation.FillNotes("Created by the SWC-135 extended journey");
        consultation.ClickSave();
        consultation.WaitForSavedConfirmation();
        consultation.WaitForVitalSignsForm();
        consultation.EnterVital("temperatureCelsius", "36.8");
        consultation.EnterVital("pulseRate", "72");
        consultation.ClickSaveVitals();
        consultation.WaitForVitalsSavedMessage();
        consultation.WaitUntilCompleteConsultationIsEnabled();
        consultation.ClickCompleteConsultation();

        var prescription = new PrescriptionPage(Driver);
        prescription.WaitUntilReady();
        Assert.True(prescription.ShowsCompletedConfirmation);
        prescription.RecordNoPrescriptionRequired();

        AppSession.LogIn(Driver, "admin.fernando");
        AppSession.GoTo(Driver, ActivityFeedPage.Path);
        var feed = new ActivityFeedPage(Driver);
        feed.WaitUntilLoaded();
        feed.WaitForEntryContainingAll(TimeSpan.FromSeconds(25), patient.FullName, "Consultation completed");

        AppSession.GoTo(Driver, DailyReportPage.Path);
        var report = new DailyReportPage(Driver);
        report.WaitForDiagnosis(diagnosis);
        Assert.True(report.ContainsDiagnosis(diagnosis));
    }
}
