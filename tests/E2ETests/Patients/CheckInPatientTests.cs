using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Patients;

// Covers SWC-15 (Check In Returning Patient) in the browser. The API-level behaviour -
// the 202, the patient-checked-in event carrying IsNewPatient false, and the unique
// constraint that keeps a duplicate same-day check-in from creating a second row - is
// already proven in docs/testing/postman/SWC-15-collection.json and by the direct Kafka
// and MySQL inspection recorded in docs/testing/SWC-15-test-results.md. What was never
// observed there, because the Vite dev server was not running during that session, is the
// rendering half of AC1 and AC2: the success wording, and the button being hidden behind
// the already-checked-in banner. That is exactly what these two tests close.
//
// Both start from patient search, not a deep link to the profile, because the story is
// "a receptionist finds a returning patient and checks them in without re-registering".
[Trait("Category", "E2E")]
public class CheckInPatientTests : SeleniumTestBase
{
    // AC1 - a patient who exists but is not in today's queue is checked in from their
    // profile and is given a queue number automatically.
    //
    // Reaching that precondition needs a direct delete: registering a patient publishes
    // patient-checked-in and QueueService queues them straight away, and no shipped
    // endpoint removes or completes an entry. See Support/QueueDatabase.cs.
    [Fact]
    public void CheckIn_WhenPatientIsNotInTodaysQueue_AssignsAQueueNumberAndConfirms()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();

        // Wait for the registration event to land before deleting, otherwise the consumer
        // can insert the row moments after the delete and put the test back in the
        // already-checked-in state it is trying to avoid.
        seed.WaitUntilWaiting(patient.PatientId);
        Assert.True(
            QueueDatabase.DeleteTodayQueueEntry(patient.PatientId),
            "expected registration to have queued the patient, so the AC1 precondition could be created by removing that row");

        var profile = OpenProfileFromSearch(patient.FullName);

        profile.WaitForCheckInButton();
        profile.ClickCheckIn();

        var banner = profile.WaitForCheckInSuccessBanner();

        Assert.Contains($"{patient.FullName} checked in.", banner);
        Assert.Matches(@"Queue: Q-\d+", banner);
        profile.WaitForCheckInButtonGone();
    }

    // AC2 - a patient already in today's queue shows the already-checked-in banner with
    // the number they were given, and offers no Check In button at all. A freshly
    // registered patient is already in this state, since registration queues them.
    [Fact]
    public void CheckIn_WhenPatientIsAlreadyInTodaysQueue_HidesTheButtonAndShowsTheQueueNumber()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        var profile = OpenProfileFromSearch(patient.FullName);

        var banner = profile.WaitForAlreadyCheckedInBanner();

        Assert.Contains("Already checked in", banner);
        Assert.Matches(@"Queue: Q-\d+", banner);
        Assert.False(profile.HasCheckInButton);
    }

    private PatientProfilePage OpenProfileFromSearch(string fullName)
    {
        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, PatientSearchPage.Path);

        var search = new PatientSearchPage(Driver);
        search.WaitUntilLoaded();
        search.Search(fullName);
        search.WaitForResultRow(fullName);
        search.OpenResult(fullName);

        var profile = new PatientProfilePage(Driver);
        profile.WaitUntilLoaded();
        return profile;
    }
}
