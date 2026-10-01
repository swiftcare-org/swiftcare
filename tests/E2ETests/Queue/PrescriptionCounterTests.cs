using E2ETests.Pages;
using E2ETests.Support;
using OpenQA.Selenium;

namespace E2ETests.Queue;

// Browser coverage for SWC-30 (View Prescription at Counter) and SWC-41 (Dispense
// Prescription). Each test completes a real consultation for its own doctor and patient
// through the APIs, then drives the receptionist's queue and prescription screens. The API
// contract, 401/403 checks and exact response bodies are covered by the SWC-30 and SWC-41
// Postman collections and the SWC-101/SWC-104 unit tests.
[Trait("Category", "E2E")]
[Collection(E2ETestCollections.SharedQueue)]
public class PrescriptionCounterTests : SeleniumTestBase
{
    // QueueService marks the entry COMPLETED from a Kafka event, so the queue screen's own
    // five-second poll needs a bounded wait before the prescription actions appear.
    private static readonly TimeSpan QueueUpdateTimeout = TimeSpan.FromSeconds(20);

    // SWC-30 AC1 and SWC-41 AC1-AC2 - the receptionist opens the prescription from the
    // queue, sees every detail, dispenses it, and the queue shows the dispensed status.
    [Fact]
    public void Receptionist_ViewsFullPrescription_AndDispensesIt()
    {
        using var seed = new SeedClient();
        var arranged = ArrangeCompletedConsultation(seed, withPrescription: true);
        var receptionist = seed.CreateUser("Receptionist");

        var queue = OpenQueueAsReceptionist(arranged, receptionist.Username, receptionist.Password);
        queue.ClickViewPrescription(arranged.Assignment.QueueNumber);

        var details = new PrescriptionDetailsPage(Driver);
        details.WaitUntilLoaded();
        Assert.Equal(arranged.Patient.FullName, details.CounterDetail("Patient"));
        Assert.Equal(arranged.Assignment.QueueNumber, details.CounterDetail("Queue number"));
        Assert.Equal(arranged.Doctor.FullName, details.CounterDetail("Doctor"));
        Assert.Equal(arranged.Assignment.RoomNumber, details.CounterDetail("Room"));
        Assert.NotEmpty(details.PrescriptionDate);
        Assert.Equal("PENDING", details.Status);
        Assert.Equal(new[] { "Amoxicillin", "Cetirizine" }, details.MedicineNames);
        Assert.Equal("500 mg", details.MedicineDetail("Amoxicillin", "Dosage"));
        Assert.Equal("Twice daily", details.MedicineDetail("Amoxicillin", "Frequency"));
        Assert.Equal("5 days", details.MedicineDetail("Amoxicillin", "Duration"));
        Assert.Equal("After meals", details.MedicineDetail("Amoxicillin", "Instructions"));
        Assert.Equal("-", details.MedicineDetail("Cetirizine", "Instructions"));

        Assert.True(details.HasDispenseButton);
        details.ClickMarkAsDispensed();
        var dispensedMessage = details.WaitForDispensedMessage();
        Assert.StartsWith($"Dispensed by {receptionist.FullName} on ", dispensedMessage);
        Assert.False(details.HasDispenseButton);
        Assert.Equal("DISPENSED", details.Status);

        // The dispensing time is read back from the server after a reload and must not shift.
        details.Refresh();
        Assert.Equal(dispensedMessage, details.WaitForDispensedMessage());
        Assert.False(details.HasDispenseButton);

        AppSession.GoTo(Driver, QueueManagementPage.Path);
        queue.WaitUntilLoaded();
        queue.WaitForDispensedStatus(arranged.Assignment.QueueNumber, QueueUpdateTimeout);
    }

    // SWC-30 AC2 - a completed patient without a saved prescription opens a waiting message,
    // not an error or a blank screen.
    [Fact]
    public void CompletedPatientWithoutPrescription_ShowsNotRecordedYet()
    {
        using var seed = new SeedClient();
        var arranged = ArrangeCompletedConsultation(seed, withPrescription: false);

        var queue = OpenQueueAsReceptionist(arranged, "reception.silva");
        Assert.Equal(
            PrescriptionDetailsPage.PathFor(arranged.Assignment.QueueId),
            queue.ViewPrescriptionPath(arranged.Assignment.QueueNumber));
        queue.ClickViewPrescription(arranged.Assignment.QueueNumber);

        var details = new PrescriptionDetailsPage(Driver);
        details.WaitUntilLoaded();
        Assert.True(details.ShowsNoPrescriptionYet);
        Assert.False(details.HasLoadError);
        Assert.False(details.HasDispenseButton);
        Assert.Equal(arranged.Patient.FullName, details.CounterDetail("Patient"));
    }

    // SWC-30 AC4-AC5 - pending prescriptions are listed oldest first, and the list shows
    // "All prescriptions dispensed today" once every one has been dispensed. The empty state
    // is reached through SeedClient.EnsureNoOtherPendingPrescriptionsToday.
    [Fact]
    public void PendingList_ShowsOldestFirst_AndIsEmptyOnceAllDispensed()
    {
        using var seed = new SeedClient();
        var first = ArrangeCompletedConsultation(seed, withPrescription: true);
        var second = ArrangeCompletedConsultation(seed, withPrescription: true);
        seed.EnsureNoOtherPendingPrescriptionsToday(first.Assignment.QueueId, second.Assignment.QueueId);

        var queue = OpenQueueAsReceptionist(second, "reception.silva");
        queue.WaitForPendingPrescription(first.Assignment.QueueNumber, QueueUpdateTimeout);
        queue.WaitForPendingPrescription(second.Assignment.QueueNumber, QueueUpdateTimeout);
        Assert.Equal(
            new[] { first.Assignment.QueueNumber, second.Assignment.QueueNumber },
            queue.PendingPrescriptionQueueNumbers);

        seed.DispensePrescription(first.PrescriptionId!);
        seed.DispensePrescription(second.PrescriptionId!);

        queue.WaitForAllPrescriptionsDispensed(QueueUpdateTimeout);
        Assert.Empty(queue.PendingPrescriptionQueueNumbers);
    }

    // SWC-41 AC3 - Doctors and Admins can open the prescription but never see the dispense
    // button. The Admin uses a separate tab because each tab keeps its own login.
    [Fact]
    public void DoctorAndAdmin_CanViewPrescription_ButCannotDispense()
    {
        using var seed = new SeedClient();
        var arranged = ArrangeCompletedConsultation(seed, withPrescription: true);
        var path = PrescriptionDetailsPage.PathFor(arranged.Assignment.QueueId);

        AppSession.LogIn(Driver, arranged.Doctor.Username, arranged.Doctor.Password);
        AppSession.GoTo(Driver, path);
        var doctorView = new PrescriptionDetailsPage(Driver);
        doctorView.WaitUntilLoaded();
        Assert.Equal("PENDING", doctorView.Status);
        Assert.Equal($"Prescribed by {arranged.Doctor.FullName}", doctorView.PrescribedByLine);
        Assert.Equal(new[] { "Amoxicillin", "Cetirizine" }, doctorView.MedicineNames);
        Assert.False(doctorView.HasDispenseButton);

        Driver.SwitchTo().NewWindow(WindowType.Tab);
        AppSession.LogIn(Driver, "admin.fernando");
        AppSession.GoTo(Driver, path);
        var adminView = new PrescriptionDetailsPage(Driver);
        adminView.WaitUntilLoaded();
        Assert.Equal("PENDING", adminView.Status);
        Assert.Equal(new[] { "Amoxicillin", "Cetirizine" }, adminView.MedicineNames);
        Assert.False(adminView.HasDispenseButton);
    }

    private sealed record ArrangedCounterCase(
        SeededUser Doctor,
        SeededPatient Patient,
        CurrentQueueAssignment Assignment,
        string? PrescriptionId);

    // The whole consultation, and the prescription when requested, is created through the
    // real APIs; the browser only drives the screens under test.
    private static ArrangedCounterCase ArrangeCompletedConsultation(SeedClient seed, bool withPrescription)
    {
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);
        seed.CallNext(doctor.Username, doctor.Password);
        var assignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        Assert.Equal(patient.PatientId, assignment.PatientId);

        var consultation = seed.CreateConsultation(assignment, doctor.Username, doctor.Password);
        seed.RecordVitalSigns(consultation.Id, doctor.Username, doctor.Password);
        seed.CompleteConsultation(consultation.Id, doctor.Username, doctor.Password);

        var prescriptionId = withPrescription
            ? seed.CreatePrescription(
                consultation,
                doctor.Username,
                doctor.Password,
                new SeededMedicine("Amoxicillin", "500 mg", "Twice daily", "5 days", "After meals"),
                new SeededMedicine("Cetirizine", "10 mg", "Once daily", "7 days"))
            : null;

        return new ArrangedCounterCase(doctor, patient, assignment, prescriptionId);
    }

    private QueueManagementPage OpenQueueAsReceptionist(
        ArrangedCounterCase arranged,
        string username,
        string? password = null)
    {
        AppSession.LogIn(Driver, username, password);
        AppSession.GoTo(Driver, QueueManagementPage.Path);
        var queue = new QueueManagementPage(Driver);
        queue.WaitUntilLoaded();
        queue.WaitForStatus(arranged.Assignment.QueueNumber, "COMPLETED", QueueUpdateTimeout);
        return queue;
    }
}
