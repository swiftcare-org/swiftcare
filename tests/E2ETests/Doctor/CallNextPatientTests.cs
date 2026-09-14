using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Doctor;

// Covers SWC-22 (Call Next Patient) in the browser. The server side - first-in-line
// selection, the identity taken from the JWT rather than from client input, the
// patient-called Kafka event, the 409 wording and the role gating - is covered by
// docs/testing/postman/SWC-22-collection.json. These tests cover what only the browser
// shows: that the click puts the assigned patient into the doctor's own current-patient
// panel with their room, takes the row out of the pool underneath it, and locks the button;
// and that a second doctor calling into a different room leaves the first doctor's panel
// exactly as it was.
//
// Each test creates its own throwaway Doctor account, for the same reason ConsultationTests
// does: no complete-consultation endpoint exists yet, so an account that has called stays
// occupied for the rest of the clinic day and a shared account would make the suite unsafe
// to re-run.
[Trait("Category", "E2E")]
public class CallNextPatientTests : SeleniumTestBase
{
    // AC1 - the call is reflected immediately in the calling doctor's own dashboard: the
    // current-patient panel names the patient and their room, the row leaves the pool, and
    // the button is locked because the doctor is now occupied.
    [Fact]
    public void CallNext_ShowsTheAssignedPatientInTheCurrentPatientPanelAndClearsTheirRow()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        var dashboard = OpenDashboardAs(doctor);
        dashboard.WaitForPatientRow(patient.FullName);

        dashboard.ClickCallNextPatient();
        var panel = dashboard.WaitForCurrentPatientPanel();
        var calledQueueNumber = DoctorDashboardPage.QueueNumberFrom(panel);

        // The panel carries the room from the doctor's own account, which the browser never
        // sent. Which patient is first in line is not asserted here: the pool is shared and
        // these classes run in parallel, so another test's doctor can legitimately take the
        // head of the queue between the arrangement and the click. First-in-line selection is
        // asserted deterministically in the SWC-22 Postman collection instead.
        Assert.Contains($"Room {doctor.RoomNumber}", dashboard.CurrentRoomText);
        Assert.Matches(@"Currently with you: Q-\d+ \S", panel);

        dashboard.WaitForWaitingRowGone(calledQueueNumber, TimeSpan.FromSeconds(10));
        dashboard.WaitUntilCallNextIsDisabled();
    }

    // AC4 - occupancy is tracked per room, so two doctors in different rooms do not block
    // each other. The second doctor calls over the API rather than in a second browser,
    // because what is being proven is that the first doctor's screen is unaffected, and that
    // is observable in the one session this test drives.
    [Fact]
    public void CallNext_ByASecondDoctorInAnotherRoom_SucceedsAndLeavesTheFirstDoctorsPanelIntact()
    {
        using var seed = new SeedClient();
        var browserDoctor = seed.CreateUser("Doctor");
        var otherDoctor = seed.CreateUser("Doctor");
        Assert.NotEqual(browserDoctor.RoomNumber, otherDoctor.RoomNumber);

        var first = seed.RegisterPatient();
        var second = seed.RegisterPatient();
        seed.WaitUntilWaiting(first.PatientId);
        seed.WaitUntilWaiting(second.PatientId);

        var dashboard = OpenDashboardAs(browserDoctor);
        dashboard.WaitForPatientRow(first.FullName);
        dashboard.ClickCallNextPatient();
        var panel = dashboard.WaitForCurrentPatientPanel();

        // A different doctor in a different room calls next while the first doctor is still
        // occupied. No 409: the block is on the room, not on the clinic.
        var otherCall = seed.CallNext(otherDoctor.Username, otherDoctor.Password);

        Assert.Equal(otherDoctor.RoomNumber, otherCall.RoomNumber);
        Assert.NotEqual(DoctorDashboardPage.QueueNumberFrom(panel), otherCall.QueueNumber);

        // Two full poll cycles, so the assertion below is made after the dashboard has had
        // every chance to react to the other doctor's call rather than before it could.
        dashboard.WaitForWaitingRowGone(otherCall.QueueNumber, TimeSpan.FromSeconds(12));

        Assert.Equal(panel, dashboard.WaitForCurrentPatientPanel());
        Assert.Contains($"Room {browserDoctor.RoomNumber}", dashboard.CurrentRoomText);
        Assert.False(dashboard.IsCallNextEnabled);
    }

    private DoctorDashboardPage OpenDashboardAs(SeededUser doctor)
    {
        var login = new LoginPage(Driver);
        login.NavigateTo();
        login.SubmitCredentials(doctor.Username, doctor.Password);
        login.WaitForRedirectAwayFromLogin();

        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        return dashboard;
    }
}
