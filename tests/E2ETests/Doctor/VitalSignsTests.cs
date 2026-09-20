using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Doctor;

// Browser coverage for SWC-25's doctor-facing form. Persistence and server-side
// BMI calculation are covered by the MedicalRecordService unit/API tests; these
// cases verify what a doctor can see and do in the actual consultation screen.
// Every test uses a unique doctor/patient and a real queue assignment.
[Trait("Category", "E2E")]
[Collection(E2ETestCollections.SharedQueue)]
public class VitalSignsTests : SeleniumTestBase
{
    [Fact]
    public void SaveAllVitalSigns_ShowsAutomaticReadOnlyBmiAndConsultationConfirmation()
    {
        using var seed = new SeedClient();
        var consultation = ArrangeSavedConsultation(seed);

        Assert.True(consultation.BmiIsOutputOnly);
        Assert.Equal(string.Empty, consultation.BmiText);

        consultation.EnterVital("heightCentimeters", "170");
        Assert.Equal(string.Empty, consultation.BmiText);
        consultation.EnterVital("weightKilograms", "68");
        Assert.Equal("23.53", consultation.BmiText);

        var measurements = new Dictionary<string, string>
        {
            ["systolicBloodPressure"] = "120",
            ["diastolicBloodPressure"] = "80",
            ["temperatureCelsius"] = "37",
            ["pulseRate"] = "75",
            ["respiratoryRate"] = "16",
            ["oxygenSaturation"] = "98",
            ["heightCentimeters"] = "170",
            ["weightKilograms"] = "68",
        };

        foreach (var (field, value) in measurements.Where(entry =>
                     entry.Key is not ("heightCentimeters" or "weightKilograms")))
        {
            consultation.EnterVital(field, value);
        }

        Assert.All(measurements, entry =>
            Assert.Equal(entry.Value, consultation.VitalValue(entry.Key)));

        consultation.ClickSaveVitals();

        Assert.Equal(
            "Measurements were linked to this consultation with a BMI of 23.53.",
            consultation.WaitForVitalsSavedMessage());
        Assert.All(measurements, entry =>
        {
            Assert.Equal(entry.Value, consultation.VitalValue(entry.Key));
            Assert.False(consultation.IsVitalInputEnabled(entry.Key));
        });
    }

    [Fact]
    public void MissingHeightOrWeight_KeepsBmiEmptyAndSavesWithoutBmi()
    {
        using var seed = new SeedClient();
        var consultation = ArrangeSavedConsultation(seed);

        consultation.EnterVital("heightCentimeters", "170");
        Assert.Equal(string.Empty, consultation.BmiText);

        consultation.EnterVital("weightKilograms", "68");
        Assert.Equal("23.53", consultation.BmiText);

        consultation.ClearVital("weightKilograms");
        Assert.Equal(string.Empty, consultation.VitalValue("weightKilograms"));
        Assert.Equal(string.Empty, consultation.BmiText);

        consultation.EnterVital("weightKilograms", "68");
        Assert.Equal("23.53", consultation.BmiText);

        consultation.ClearVital("heightCentimeters");
        Assert.Equal(string.Empty, consultation.VitalValue("heightCentimeters"));
        Assert.Equal(string.Empty, consultation.BmiText);

        consultation.ClickSaveVitals();
        Assert.Equal(
            "Measurements were linked to this consultation.",
            consultation.WaitForVitalsSavedMessage());
        Assert.Equal(string.Empty, consultation.BmiText);
    }

    [Fact]
    public void UnusualPositiveValues_ShowWarningsWithoutBlockingSave()
    {
        using var seed = new SeedClient();
        var consultation = ArrangeSavedConsultation(seed);

        consultation.EnterVital("temperatureCelsius", "50");
        consultation.EnterVital("pulseRate", "300");

        Assert.Equal("Please verify this value", consultation.VitalWarning("temperatureCelsius"));
        Assert.Equal("Please verify this value", consultation.VitalWarning("pulseRate"));
        Assert.True(consultation.IsSaveVitalsEnabled);

        consultation.ClickSaveVitals();

        Assert.Equal(
            "Measurements were linked to this consultation.",
            consultation.WaitForVitalsSavedMessage());
        Assert.Equal("50", consultation.VitalValue("temperatureCelsius"));
        Assert.Equal("300", consultation.VitalValue("pulseRate"));
    }

    private ConsultationPage ArrangeSavedConsultation(SeedClient seed)
    {
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        var login = new LoginPage(Driver);
        login.NavigateTo();
        login.SubmitCredentials(doctor.Username, doctor.Password);
        login.WaitForRedirectAwayFromLogin();

        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        dashboard.WaitForPatientRow(patient.FullName);
        seed.EnsureNextWaitingPatientIs(patient.PatientId);
        dashboard.ClickCallNextPatient();
        var currentPanel = dashboard.WaitForCurrentPatientPanel();

        var assignment = seed.GetCurrentForDoctor(doctor.Username, doctor.Password);
        Assert.Equal("IN_CONSULTATION", assignment.Status);
        Assert.Equal(patient.PatientId, assignment.PatientId);
        Assert.Equal(assignment.QueueNumber, DoctorDashboardPage.QueueNumberFrom(currentPanel));

        dashboard.ClickRecordConsultation();
        var consultation = new ConsultationPage(Driver);
        consultation.WaitUntilLoaded();
        Assert.Contains(assignment.QueueNumber, consultation.CurrentConsultationContext);
        Assert.Contains(patient.FullName, consultation.CurrentConsultationContext);
        Assert.Contains($"Room {assignment.RoomNumber}", consultation.CurrentConsultationContext);
        consultation.FillSymptoms("SWC-25 E2E vital-sign visit");
        consultation.FillDiagnosis("SWC-25 E2E clinical assessment");
        consultation.ClickSave();
        consultation.WaitForSavedConfirmation();
        consultation.WaitForVitalSignsForm();
        return consultation;
    }
}
