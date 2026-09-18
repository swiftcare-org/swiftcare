using E2ETests.Pages;
using E2ETests.Support;
using OpenQA.Selenium;

namespace E2ETests.SmokeJourneys;

// The Sprint 2 cross-story smoke journey: one clinic day walked end to end across SWC-15,
// SWC-20, SWC-21, SWC-22, SWC-24, SWC-18 and SWC-23, in the order a real morning runs.
//
// It asserts no acceptance criterion the per-story classes already cover. Its whole job is
// the handoffs those classes cannot see: every per-story test seeds its preconditions over
// HTTP and drives one screen, so nothing else in this suite would catch state that fails to
// carry from one screen to the next - a check-in whose queue number never reaches the
// receptionist's queue view, a patient the doctor's pool never learns about, a call the
// public display never picks up, or a condition added in one session that a second session
// does not see.
//
// Three browsing contexts are open at once, because that is the only way to observe a
// handoff rather than a reload: the receptionist's tab, the doctor's tab, and a public
// display tab opened before the call is made and never navigated again afterwards.
[Trait("Category", "E2E")]
[Trait("Category", "Smoke")]
[Collection(E2ETestCollections.SharedQueue)]
public class ClinicDayJourneyTests : SeleniumTestBase
{
    [Fact]
    public void ClinicDay_CheckInFlowsThroughQueueCallConsultationConditionAndPublicDisplay()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();

        // The journey has to start from a genuine returning patient, so the entry
        // registration created is removed and the browser check-in below is the real one.
        // See Support/QueueDatabase.cs for why no API can produce this state.
        seed.WaitUntilWaiting(patient.PatientId);
        Assert.True(
            QueueDatabase.DeleteTodayQueueEntry(patient.PatientId),
            "expected registration to have queued the patient, so the journey could start from a returning one");

        var receptionTab = Driver.CurrentWindowHandle;

        // --- SWC-15: the receptionist finds the returning patient and checks them in ---
        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, PatientSearchPage.Path);

        var search = new PatientSearchPage(Driver);
        search.WaitUntilLoaded();
        search.Search(patient.FullName);
        search.WaitForResultRow(patient.FullName);
        search.OpenResult(patient.FullName);

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        profile.WaitForCheckInButton();
        profile.ClickCheckIn();
        var queueNumber = PatientProfilePage.QueueNumberFrom(profile.WaitForCheckInSuccessBanner());

        // --- SWC-20: the number just issued reaches the full queue, reached by navigation
        // rather than a deep link, so a redirect that dropped the session would fail here ---
        AppSession.ClickNavLink(Driver, "/reception");
        AppSession.ClickNavLink(Driver, QueueManagementPage.Path);

        var queue = new QueueManagementPage(Driver);
        queue.WaitUntilLoaded();
        queue.WaitForQueueRow(queueNumber);
        Assert.Equal(patient.FullName, queue.PatientNameFor(queueNumber));
        Assert.Contains("WAITING", queue.StatusFor(queueNumber));

        // --- SWC-23: the display is opened now, before any call, and is never navigated
        // again, so the only way it can show the call later is its own poll ---
        var displayTab = Driver.SwitchTo().NewWindow(WindowType.Tab).CurrentWindowHandle;
        var display = new WaitingRoomDisplayPage(Driver);
        display.NavigateTo();
        Assert.Null(display.GetQueueNumberForRoom(doctor.RoomNumber!));

        // --- SWC-21: a doctor who has never touched this patient sees them in the shared
        // pool, under the very number the receptionist's check-in issued ---
        var doctorTab = Driver.SwitchTo().NewWindow(WindowType.Tab).CurrentWindowHandle;
        var login = new LoginPage(Driver);
        login.NavigateTo();
        login.SubmitCredentials(doctor.Username, doctor.Password);
        login.WaitForRedirectAwayFromLogin();

        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        Assert.Equal(queueNumber, dashboard.WaitForPatientRow(patient.FullName));

        // --- SWC-22: the call lands in this doctor's own panel. Which patient is taken is
        // whoever is first in line on a queue shared with every other test and run, so the
        // journey follows the number the panel reports from here on rather than assuming it
        // is the one checked in above ---
        dashboard.ClickCallNextPatient();
        var calledQueueNumber = DoctorDashboardPage.QueueNumberFrom(dashboard.WaitForCurrentPatientPanel());
        Assert.Contains($"Room {doctor.RoomNumber}", dashboard.CurrentRoomText);

        // --- SWC-24: the consultation form is opened from the panel, and the current
        // patient it works on is state the call-next click wrote, not anything re-fetched ---
        dashboard.ClickRecordConsultation();

        var consultation = new ConsultationPage(Driver);
        consultation.WaitUntilLoaded();
        consultation.FillSymptoms("QA E2E journey symptoms - persistent cough since Monday");
        consultation.FillExaminationFindings("QA E2E journey findings - chest clear on auscultation");
        consultation.FillDiagnosis("QA E2E journey diagnosis - viral upper respiratory infection");
        consultation.FillNotes("QA E2E journey notes - review in one week if unchanged");
        consultation.ClickSave();
        consultation.WaitForSavedConfirmation();

        // --- SWC-18: the receptionist adds a chronic condition in their own still-live
        // session, and the doctor's separate session picks it up ---
        Driver.SwitchTo().Window(receptionTab);
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var profileForCondition = new PatientProfilePage(Driver);
        profileForCondition.WaitUntilLoaded();
        profileForCondition.AddCondition("Hypertension", "Added during the QA E2E clinic-day journey.");
        profileForCondition.WaitForConditionRow("Hypertension");

        Driver.SwitchTo().Window(doctorTab);
        AppSession.GoTo(Driver, $"/patients/{patient.PatientId}");

        var doctorProfile = new PatientProfilePage(Driver);
        doctorProfile.WaitUntilLoaded();
        doctorProfile.WaitForChronicConditionAlertContaining("Hypertension");

        // --- SWC-23 again: the display tab, untouched since before the call, has brought
        // the room assignment in by itself ---
        Driver.SwitchTo().Window(displayTab);
        display.WaitForRoomAssignment(doctor.RoomNumber!, calledQueueNumber, TimeSpan.FromSeconds(15));
    }
}
