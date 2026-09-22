using E2ETests.Pages;
using E2ETests.Support;
using OpenQA.Selenium;

namespace E2ETests.Doctor;

// Browser coverage for SWC-26 (Complete Consultation) and SWC-38 (Auto Complete Queue
// Entry) together, per SWC-110's combined test group: completing a consultation is the one
// user action that drives both the doctor's own screen and, through the
// consultation-completed Kafka event, the receptionist's queue and the doctor's own
// current-patient panel. Server-side completion, EventId idempotency and the exact
// CompletedAt value are covered by docs/qa/SwiftCare SWC-38 Collection.json and the
// SWC-102 unit tests; these cases verify only what a browser can see.
[Trait("Category", "E2E")]
[Collection(E2ETestCollections.SharedQueue)]
public class ConsultationCompletionTests : SeleniumTestBase
{
    // SWC-26 AC - Complete Consultation stays disabled with an explanatory message until
    // vital signs are saved, then completing navigates the doctor to the prescription
    // screen. SWC-29's own entry form is out of scope here: ConsultationPage.tsx today
    // routes to the SWC-29 placeholder until that story lands.
    [Fact]
    public void CompleteConsultation_IsBlockedUntilVitalsSaved_ThenNavigatesToPrescription()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        AppSession.LogIn(Driver, doctor.Username, doctor.Password);
        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        dashboard.WaitForPatientRow(patient.FullName);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);
        dashboard.ClickCallNextPatient();
        dashboard.WaitForCurrentPatientPanel();
        dashboard.ClickRecordConsultation();

        var consultation = new ConsultationPage(Driver);
        consultation.WaitUntilLoaded();
        consultation.FillSymptoms("SWC-26 E2E completion symptoms");
        consultation.FillDiagnosis("SWC-26 E2E completion diagnosis");
        consultation.ClickSave();
        consultation.WaitForSavedConfirmation();
        consultation.WaitForVitalSignsForm();

        Assert.False(consultation.IsCompleteConsultationEnabled);
        Assert.Equal("Please save vital signs first", consultation.CompleteConsultationBlockedMessage);

        consultation.EnterVital("pulseRate", "72");
        consultation.ClickSaveVitals();
        consultation.WaitForVitalsSavedMessage();

        consultation.WaitUntilCompleteConsultationIsEnabled();
        Assert.Null(consultation.CompleteConsultationBlockedMessage);

        consultation.ClickCompleteConsultation();

        var prescription = new PrescriptionPlaceholderPage(Driver);
        prescription.WaitUntilLoaded();
        Assert.True(prescription.ShowsCompletedConfirmation);
    }

    // SWC-38 AC - completing a consultation clears the doctor's own current-patient panel,
    // re-enables Call Next Patient, flips the receptionist's queue row to COMPLETED with the
    // disabled View Prescription action, and lets the doctor call the next patient. Two tabs
    // stay open throughout, the same way ClinicDayJourneyTests observes a handoff: each
    // screen has to pick the change up through its own poll, not through a fresh navigation.
    [Fact]
    public void AutomaticCompletion_ClearsDoctorPanelAndUpdatesReceptionistQueue()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient1 = seed.RegisterPatient();
        var patient2 = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient1.PatientId);
        seed.EnsureNextWaitingPatientIs(patient1.PatientId);
        seed.CallNext(doctor.Username, doctor.Password);
        var assignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        Assert.Equal(patient1.PatientId, assignment.PatientId);

        var consultation = seed.CreateConsultation(assignment, doctor.Username, doctor.Password);
        seed.RecordVitalSigns(consultation.Id, doctor.Username, doctor.Password);
        seed.WaitUntilWaiting(patient2.PatientId);

        var doctorTab = Driver.CurrentWindowHandle;
        AppSession.LogIn(Driver, doctor.Username, doctor.Password);
        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        dashboard.WaitForCurrentPatientPanel();

        var receptionistTab = Driver.SwitchTo().NewWindow(WindowType.Tab).CurrentWindowHandle;
        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, QueueManagementPage.Path);
        var queue = new QueueManagementPage(Driver);
        queue.WaitUntilLoaded();
        queue.WaitForQueueRow(assignment.QueueNumber);
        Assert.Contains("IN CONSULTATION", queue.StatusFor(assignment.QueueNumber));
        Assert.False(queue.HasViewPrescriptionButton(assignment.QueueNumber));

        seed.CompleteConsultation(consultation.Id, doctor.Username, doctor.Password);

        Driver.SwitchTo().Window(doctorTab);
        dashboard.WaitUntilCurrentPatientPanelGone(TimeSpan.FromSeconds(20));
        dashboard.WaitUntilCallNextIsEnabled();

        Driver.SwitchTo().Window(receptionistTab);
        queue.WaitForStatus(assignment.QueueNumber, "COMPLETED", TimeSpan.FromSeconds(20));
        Assert.True(queue.HasViewPrescriptionButton(assignment.QueueNumber));
        Assert.False(queue.IsViewPrescriptionEnabled(assignment.QueueNumber));

        Driver.SwitchTo().Window(doctorTab);
        dashboard.WaitForPatientRow(patient2.FullName);
        seed.EnsureNextWaitingPatientIs(patient2.PatientId);
        dashboard.ClickCallNextPatient();
        dashboard.WaitForCurrentPatientPanel();

        var secondAssignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        Assert.Equal(patient2.PatientId, secondAssignment.PatientId);
    }
}
