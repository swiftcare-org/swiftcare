using E2ETests.Pages;
using E2ETests.Support;
using OpenQA.Selenium;

namespace E2ETests.SmokeJourneys;

// The Sprint 3 cross-story journey for SWC-110: one patient walked from check-in to a
// dispensed prescription across SWC-92, SWC-28, SWC-25, SWC-26, SWC-38, SWC-29, SWC-30 and
// SWC-41, in the order a real consultation runs.
//
// It asserts no acceptance criterion the per-story classes already cover in depth. Its job is
// the handoffs between screens: the same queue number, patient, doctor, room and prescription
// must carry from the receptionist's check-in through the doctor's screens and back to the
// counter. Two tabs stay open - the receptionist's and the doctor's - so each role sees the
// other's changes through its own screens rather than through a shared browser session.
[Trait("Category", "E2E")]
[Trait("Category", "Smoke")]
[Collection(E2ETestCollections.SharedQueue)]
public class ClinicalJourneyTests : SeleniumTestBase
{
    // QueueService applies check-in and completion from Kafka events, so screens that show
    // them need a bounded wait.
    private static readonly TimeSpan QueueUpdateTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public void ClinicalJourney_CheckInThroughDispensedPrescription_PreservesPatientContext()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var receptionist = seed.CreateUser("Receptionist");
        var patient = seed.RegisterPatient();
        seed.AddAllergy(patient.PatientId, "Penicillin", "Severe");
        seed.AddChronicCondition(patient.PatientId, "Type 2 Diabetes", "2022-01-15");

        // The journey starts from a returning patient, so the entry registration created is
        // removed and the browser check-in below is the real one (see Support/QueueDatabase.cs).
        seed.WaitUntilWaiting(patient.PatientId);
        Assert.True(
            QueueDatabase.DeleteTodayQueueEntry(patient.PatientId),
            "expected registration to have queued the patient, so the journey could start from a returning one");

        // --- Patient check-in (receptionist tab) ---
        var receptionTab = Driver.CurrentWindowHandle;
        AppSession.LogIn(Driver, receptionist.Username, receptionist.Password);
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");
        var checkInProfile = new PatientProfilePage(Driver);
        checkInProfile.WaitUntilLoaded();
        checkInProfile.WaitForCheckInButton();
        checkInProfile.ClickCheckIn();
        var queueNumber = PatientProfilePage.QueueNumberFrom(checkInProfile.WaitForCheckInSuccessBanner());

        // --- Doctor calls the patient (doctor tab) ---
        seed.WaitUntilWaiting(patient.PatientId);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);
        var doctorTab = Driver.SwitchTo().NewWindow(WindowType.Tab).CurrentWindowHandle;
        AppSession.LogIn(Driver, doctor.Username, doctor.Password);
        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        Assert.Equal(queueNumber, dashboard.WaitForPatientRow(patient.FullName));
        dashboard.ClickCallNextPatient();
        var currentPanel = dashboard.WaitForCurrentPatientPanel();
        Assert.Equal(queueNumber, DoctorDashboardPage.QueueNumberFrom(currentPanel));
        Assert.Contains(patient.FullName, currentPanel);
        Assert.Contains($"Room {doctor.RoomNumber}", dashboard.CurrentRoomText);

        var assignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        Assert.Equal(patient.PatientId, assignment.PatientId);
        Assert.Equal(queueNumber, assignment.QueueNumber);

        // --- Doctor opens the patient profile and sees the medical alerts ---
        dashboard.ClickCurrentPatientName();
        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        Assert.Equal(patient.PatientId, profile.PatientId, ignoreCase: true);
        profile.WaitForMedicalAlertCount(2);
        Assert.Equal(new[] { "Allergy Alert", "Chronic Condition Alert" }, profile.MedicalAlertLabelsInOrder);
        Assert.Contains(profile.MedicalAlertMessagesInOrder, message => message.Contains("Penicillin"));
        Assert.Contains(profile.MedicalAlertMessagesInOrder, message => message.Contains("Type 2 Diabetes"));

        // --- Doctor opens the consultation for the same patient ---
        AppSession.GoTo(Driver, DoctorDashboardPage.Path);
        dashboard.WaitUntilLoaded();
        dashboard.WaitForCurrentPatientPanel();
        dashboard.ClickRecordConsultation();
        var consultation = new ConsultationPage(Driver);
        consultation.WaitUntilLoaded();
        Assert.Contains(queueNumber, consultation.CurrentConsultationContext);
        Assert.Contains(patient.FullName, consultation.CurrentConsultationContext);
        Assert.Contains($"Room {doctor.RoomNumber}", consultation.CurrentConsultationContext);
        consultation.FillSymptoms("SWC-110 journey symptoms - fever and sore throat for two days");
        consultation.FillDiagnosis("SWC-110 journey diagnosis - acute pharyngitis");
        consultation.ClickSave();
        consultation.WaitForSavedConfirmation();

        // --- Doctor records vital signs ---
        consultation.WaitForVitalSignsForm();
        consultation.EnterVital("temperatureCelsius", "38");
        consultation.EnterVital("pulseRate", "88");
        consultation.EnterVital("heightCentimeters", "170");
        consultation.EnterVital("weightKilograms", "68");
        consultation.ClickSaveVitals();
        consultation.WaitForVitalsSavedMessage();

        // --- Doctor completes the consultation ---
        consultation.WaitUntilCompleteConsultationIsEnabled();
        consultation.ClickCompleteConsultation();

        // --- Doctor creates the prescription for the same consultation ---
        var prescription = new PrescriptionPage(Driver);
        prescription.WaitUntilReady();
        Assert.True(prescription.ShowsCompletedConfirmation);
        Assert.Equal($"{queueNumber} {patient.FullName}", prescription.ConsultationContext);
        Assert.Contains(
            prescription.AllergyWarningTexts,
            text => text.Contains("WARNING: Patient is allergic to Penicillin (Severe)"));
        prescription.ClickAddMedicine();
        prescription.FillDraftMedicine(1, "Azithromycin", "500 mg", "Once daily", "3 days", "Take one hour before food");
        prescription.ClickAddMedicine();
        prescription.FillDraftMedicine(2, "Paracetamol", "500 mg", "Every six hours when needed", "3 days");
        prescription.ClickSavePrescription();
        prescription.WaitForSavedPrescription();
        prescription.WaitForMessage("Prescription saved successfully.");
        Assert.Equal("PENDING", prescription.LatestHistoryStatus);

        // --- Queue entry becomes completed and the prescription waits at the counter ---
        Driver.SwitchTo().Window(receptionTab);
        AppSession.GoTo(Driver, QueueManagementPage.Path);
        var queue = new QueueManagementPage(Driver);
        queue.WaitUntilLoaded();
        queue.WaitForStatus(queueNumber, "COMPLETED", QueueUpdateTimeout);
        queue.WaitForPendingPrescription(queueNumber, QueueUpdateTimeout);
        Assert.Equal(
            PrescriptionDetailsPage.PathFor(assignment.QueueId),
            queue.ViewPrescriptionPath(queueNumber));

        // --- Receptionist views the doctor's prescription ---
        queue.ClickViewPrescription(queueNumber);
        var counter = new PrescriptionDetailsPage(Driver);
        counter.WaitUntilLoaded();
        Assert.Equal(patient.FullName, counter.CounterDetail("Patient"));
        Assert.Equal(queueNumber, counter.CounterDetail("Queue number"));
        Assert.Equal(doctor.FullName, counter.CounterDetail("Doctor"));
        Assert.Equal(doctor.RoomNumber, counter.CounterDetail("Room"));
        Assert.Equal("PENDING", counter.Status);
        Assert.Equal(new[] { "Azithromycin", "Paracetamol" }, counter.MedicineNames);
        Assert.Equal("Take one hour before food", counter.MedicineDetail("Azithromycin", "Instructions"));

        // --- Receptionist dispenses the prescription ---
        counter.ClickMarkAsDispensed();
        Assert.StartsWith($"Dispensed by {receptionist.FullName} on ", counter.WaitForDispensedMessage());
        Assert.False(counter.HasDispenseButton);

        AppSession.GoTo(Driver, QueueManagementPage.Path);
        queue.WaitUntilLoaded();
        queue.WaitForDispensedStatus(queueNumber, QueueUpdateTimeout);

        // --- The doctor's view of the same prescription is now read-only ---
        Driver.SwitchTo().Window(doctorTab);
        prescription.Refresh();
        prescription.WaitForSavedPrescription();
        Assert.True(prescription.ShowsDispensedNotice);
        Assert.Equal("DISPENSED", prescription.LatestHistoryStatus);
    }
}
