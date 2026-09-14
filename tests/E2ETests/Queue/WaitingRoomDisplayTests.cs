using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Queue;

// Covers the two SWC-23 (Public Waiting Room Display) acceptance criteria that cannot
// be proven through Postman or a static screenshot: that the display genuinely needs
// no login, and that an already-open tab updates itself with no manual reload. Every
// other acceptance criterion (room mapping, next-three ordering, no PHI in the
// response, responsive layout) is covered by the SWC-23 Postman collection and manual
// screenshots - see docs/testing/SWC-23-test-results.md.
[Trait("Category", "E2E")]
public class WaitingRoomDisplayTests : SeleniumTestBase
{
    // Scenario 4 - No login required: a fresh browser with no session of any kind
    // lands directly on the display, with no redirect to /login and no token ever
    // issued.
    [Fact]
    public void Display_WithNoSession_RendersImmediatelyWithNoRedirectToLogin()
    {
        var display = new WaitingRoomDisplayPage(Driver);

        display.NavigateTo();

        Assert.Contains(WaitingRoomDisplayPage.Path, Driver.Url);
        Assert.DoesNotContain("/login", Driver.Url);
        Assert.Null(GetStoredToken());
    }

    // Scenario 5 - Auto refresh: the display must update automatically when a
    // consultation is called, with no manual reload. The precondition (a throwaway
    // doctor account and a waiting patient) is set up over HTTP via SeedClient so the
    // Selenium steps stay focused on the one behaviour under test - see
    // Support/SeedClient.cs.
    [Fact]
    public void Display_WhenPatientIsCalled_UpdatesTheOpenTabWithoutReload()
    {
        using var seed = new SeedClient();
        var doctor = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        var display = new WaitingRoomDisplayPage(Driver);
        display.NavigateTo();
        Assert.Null(display.GetQueueNumberForRoom(doctor.RoomNumber!));

        var called = seed.CallNext(doctor.Username, doctor.Password);

        // No Navigate call between the assertion above and this wait - see
        // WaitForRoomAssignment's comment. Ten seconds covers one full 5-second poll
        // cycle plus margin for the request itself.
        display.WaitForRoomAssignment(doctor.RoomNumber!, called.QueueNumber, TimeSpan.FromSeconds(10));
    }
}
