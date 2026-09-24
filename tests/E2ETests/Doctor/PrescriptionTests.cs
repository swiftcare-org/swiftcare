using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Doctor;

// Browser coverage for SWC-29 (Create Prescription) and SWC-40 (Add and Remove Medicines).
// Each test completes a real consultation for its own doctor and patient, then reaches the
// Prescription page through Complete Consultation. Request validation, ownership, 401/403
// and exact response bodies are covered by the SWC-29 and SWC-40 Postman collections and the
// SWC-100/SWC-103 unit tests; these cases verify what the doctor sees and can do.
[Trait("Category", "E2E")]
[Collection(E2ETestCollections.SharedQueue)]
public class PrescriptionTests : SeleniumTestBase
{
    // SWC-29 AC1-AC4 - the allergy warning is advisory, an empty prescription is blocked,
    // and a saved prescription is PENDING and listed under previous prescriptions, also
    // after a refresh reloads it from the server.
    [Fact]
    public void CreatePrescription_ShowsAdvisoryAllergyWarning_BlocksEmptySave_AndSavesAsPending()
    {
        using var seed = new SeedClient();
        var arranged = ArrangeCompletedConsultation(seed, withAllergy: true);
        var prescription = arranged.Page;

        Assert.Contains(
            prescription.AllergyWarningTexts,
            text => text.Contains("WARNING: Patient is allergic to Penicillin (Severe)"));
        Assert.True(prescription.ShowsNoPreviousPrescriptions);

        prescription.ClickSavePrescription();
        Assert.Equal("Add at least one medicine", prescription.WaitForDraftMessage());
        Assert.Equal(0, prescription.DraftMedicineCount);

        prescription.ClickAddMedicine();
        prescription.FillDraftMedicine(1, "Amoxicillin", "500 mg", "Twice daily", "5 days", "After meals");
        prescription.ClickAddMedicine();
        prescription.FillDraftMedicine(2, "Cetirizine", "10 mg", "Once daily", "7 days");
        prescription.ClickSavePrescription();

        var expected = new[] { "Amoxicillin", "Cetirizine" };
        prescription.WaitForSavedPrescription();
        prescription.WaitForMessage("Prescription saved successfully.");
        Assert.Equal(expected, prescription.SavedMedicineNames);
        Assert.Equal("PENDING", prescription.LatestHistoryStatus);
        Assert.Equal($"Prescribed by {arranged.Doctor.FullName}", prescription.LatestHistoryDoctorLine);
        Assert.Equal(expected, prescription.LatestHistoryMedicineNames);

        prescription.Refresh();
        prescription.WaitForSavedPrescription();
        Assert.Equal(expected, prescription.SavedMedicineNames);
        Assert.Equal("PENDING", prescription.LatestHistoryStatus);
        Assert.True(prescription.HasBackToDashboardLink);
    }

    // SWC-40 AC2-AC3 before the first save - removing a draft medicine asks for
    // confirmation, cancelling keeps it, and the last draft medicine cannot be removed.
    [Fact]
    public void DraftMedicines_RequireConfirmationToRemove_AndKeepAtLeastOne()
    {
        using var seed = new SeedClient();
        var prescription = ArrangeCompletedConsultation(seed).Page;

        prescription.ClickAddMedicine();
        prescription.FillDraftMedicine(1, "Amoxicillin", "500 mg", "Twice daily", "5 days");
        prescription.ClickAddMedicine();
        prescription.FillDraftMedicine(2, "Cetirizine", "10 mg", "Once daily", "7 days");

        prescription.ClickRemoveDraftMedicine(2);
        Assert.Equal("Remove Cetirizine from this prescription?", prescription.WaitForRemovalPrompt());
        prescription.CancelRemoval();
        Assert.False(prescription.HasRemovalPrompt);
        Assert.Equal(2, prescription.DraftMedicineCount);

        prescription.ClickRemoveDraftMedicine(2);
        prescription.WaitForRemovalPrompt();
        prescription.ConfirmRemoval();
        Assert.Equal(1, prescription.DraftMedicineCount);

        prescription.ClickRemoveDraftMedicine(1);
        prescription.WaitForRemovalPrompt();
        prescription.ConfirmRemoval();
        Assert.Equal("Prescription must have at least one medicine", prescription.WaitForDraftMessage());
        Assert.Equal(1, prescription.DraftMedicineCount);
    }

    // SWC-40 AC1-AC3 on a saved prescription - a medicine with every field is appended,
    // removal needs confirmation, the last medicine stays, and the changes survive a refresh.
    [Fact]
    public void SavedPrescription_AddsAndRemovesMedicines_AndKeepsAtLeastOne()
    {
        using var seed = new SeedClient();
        var arranged = ArrangeCompletedConsultation(seed);
        var prescription = arranged.Page;
        seed.CreatePrescription(
            arranged.Consultation,
            arranged.Doctor.Username,
            arranged.Doctor.Password,
            new SeededMedicine("Amoxicillin", "500 mg", "Twice daily", "5 days", "After meals"),
            new SeededMedicine("Cetirizine", "10 mg", "Once daily", "7 days"));

        prescription.Refresh();
        prescription.WaitForSavedMedicineNames("Amoxicillin", "Cetirizine");

        prescription.AddSavedMedicine("Paracetamol", "500 mg", "Three times daily", "3 days", "If fever persists");
        prescription.WaitForMessage("Medicine added successfully.");
        prescription.WaitForSavedMedicineNames("Amoxicillin", "Cetirizine", "Paracetamol");
        Assert.Equal(new[] { "Amoxicillin", "Cetirizine", "Paracetamol" }, prescription.LatestHistoryMedicineNames);

        prescription.ClickRemoveSavedMedicine("Cetirizine");
        Assert.Equal("Remove Cetirizine from this prescription?", prescription.WaitForRemovalPrompt());
        prescription.CancelRemoval();
        Assert.Equal(new[] { "Amoxicillin", "Cetirizine", "Paracetamol" }, prescription.SavedMedicineNames);

        prescription.ClickRemoveSavedMedicine("Cetirizine");
        prescription.WaitForRemovalPrompt();
        prescription.ConfirmRemoval();
        prescription.WaitForMessage("Medicine removed successfully.");
        prescription.WaitForSavedMedicineNames("Amoxicillin", "Paracetamol");

        prescription.ClickRemoveSavedMedicine("Paracetamol");
        prescription.WaitForRemovalPrompt();
        prescription.ConfirmRemoval();
        prescription.WaitForSavedMedicineNames("Amoxicillin");

        prescription.ClickRemoveSavedMedicine("Amoxicillin");
        prescription.WaitForRemovalPrompt();
        prescription.ConfirmRemoval();
        prescription.WaitForMessage("Prescription must have at least one medicine");
        Assert.Equal(new[] { "Amoxicillin" }, prescription.SavedMedicineNames);

        prescription.Refresh();
        prescription.WaitForSavedMedicineNames("Amoxicillin");
        Assert.Equal(new[] { "Amoxicillin" }, prescription.LatestHistoryMedicineNames);
    }

    // SWC-40 AC4 - a DISPENSED prescription is read-only. SWC-41 dispensing does not exist
    // yet, so only this test's own prescription is marked DISPENSED in the database.
    [Fact]
    public void DispensedPrescription_IsReadOnly()
    {
        using var seed = new SeedClient();
        var arranged = ArrangeCompletedConsultation(seed);
        var prescription = arranged.Page;
        var prescriptionId = seed.CreatePrescription(
            arranged.Consultation,
            arranged.Doctor.Username,
            arranged.Doctor.Password,
            new SeededMedicine("Amoxicillin", "500 mg", "Twice daily", "5 days"),
            new SeededMedicine("Cetirizine", "10 mg", "Once daily", "7 days"));
        PrescriptionDatabase.MarkDispensed(prescriptionId, arranged.Patient.PatientId);

        prescription.Refresh();
        prescription.WaitForSavedMedicineNames("Amoxicillin", "Cetirizine");

        Assert.True(prescription.ShowsDispensedNotice);
        Assert.False(prescription.HasRemoveButtons);
        Assert.False(prescription.HasAddMedicineButton);
        Assert.Equal("DISPENSED", prescription.LatestHistoryStatus);
        Assert.True(prescription.HasBackToDashboardLink);
    }

    private sealed record ArrangedPrescription(
        SeededUser Doctor,
        SeededPatient Patient,
        SeededConsultation Consultation,
        PrescriptionPage Page);

    // The consultation and vital signs are seeded over HTTP; completion is clicked in the
    // browser because the Prescription page receives its consultation context from that
    // navigation.
    private ArrangedPrescription ArrangeCompletedConsultation(SeedClient seed, bool withAllergy = false)
    {
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        if (withAllergy)
        {
            seed.AddAllergy(patient.PatientId, "Penicillin", "Severe");
        }

        seed.WaitUntilWaiting(patient.PatientId);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);
        seed.CallNext(doctor.Username, doctor.Password);
        var assignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        Assert.Equal(patient.PatientId, assignment.PatientId);

        var consultation = seed.CreateConsultation(assignment, doctor.Username, doctor.Password);
        seed.RecordVitalSigns(consultation.Id, doctor.Username, doctor.Password);

        AppSession.LogIn(Driver, doctor.Username, doctor.Password);
        AppSession.GoTo(Driver, "/doctor/consultation");
        var consultationPage = new ConsultationPage(Driver);
        consultationPage.WaitUntilLoaded();
        consultationPage.WaitUntilCompleteConsultationIsEnabled();
        consultationPage.ClickCompleteConsultation();

        var prescription = new PrescriptionPage(Driver);
        prescription.WaitUntilReady();
        Assert.True(prescription.ShowsCompletedConfirmation);
        return new ArrangedPrescription(doctor, patient, consultation, prescription);
    }
}
