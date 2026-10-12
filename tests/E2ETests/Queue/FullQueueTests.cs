using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Queue;

// Covers SWC-20 (View Full Queue Today) in the browser. The API contract - the field set,
// strict queue-number ordering and the absence of PHI in the payload - is covered by
// docs/testing/postman/SWC-20-collection.json. These tests cover the two things only a
// browser can see: that the rendered table really does carry every column the criterion
// names, with the patient's name resolved live from PatientService and the WAITING badge
// drawn, and that the page brings in a new check-in on its own poll with no reload.
//
// AC4's empty state is deliberately not automated. Reaching it means clearing every entry
// in today's queue, including records not owned by this test run. The suite only deletes
// rows for patients it created, so the empty state stays covered by the unit test
// GetTodayWhenNoPatientsAreQueuedReturnsEmptyCollection and by the manual pass recorded in
// docs/testing/SWC-20-test-results.md, TC-04.
[Trait("Category", "E2E")]
[Collection(E2ETestCollections.SharedQueue)]
public class FullQueueTests : SeleniumTestBase
{
    // AC1 and AC2 - the full column contract and the status badge, read off the rendered
    // row for a patient this test registered itself.
    [Fact]
    public void TodayQueue_ShowsEveryColumnAndTheWaitingBadgeForACheckedInPatient()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, QueueManagementPage.Path);

        var queue = new QueueManagementPage(Driver);
        queue.WaitUntilLoaded();

        // The table itself only renders once at least one row exists (QueueManagementPage.tsx
        // shows the empty-state placeholder instead until then), so the row has to be waited
        // for before the header assertion can rely on a <table> being on the page at all.
        var queueNumber = queue.WaitForPatientRow(patient.FullName);

        Assert.Equal(
            new[] { "Queue Number", "Patient", "Check-in Time", "Status", "Room", "Doctor", "Prescription" },
            queue.ColumnHeadings);

        Assert.Equal(patient.FullName, queue.PatientNameFor(queueNumber));
        Assert.NotEmpty(queue.CheckInTimeFor(queueNumber));
        Assert.Contains("WAITING", queue.StatusFor(queueNumber));

        // Room and doctor are only filled in once a doctor calls the patient, and
        // prescription status has no API behind it until SWC-30; all three render the
        // documented placeholder until then.
        Assert.NotEmpty(queue.RoomFor(queueNumber));
        Assert.NotEmpty(queue.DoctorFor(queueNumber));
        Assert.NotEmpty(queue.PrescriptionFor(queueNumber));

        // AC1's ordering, asserted over whatever the table holds rather than over this
        // test's own row alone, so an entry another test added out of order still fails it.
        var numbers = queue.QueueNumbersInOrder.Select(ParseQueueNumber).ToList();
        Assert.Equal(numbers.OrderBy(value => value), numbers);
    }

    // AC3 - the page refreshes itself every five seconds, so a patient registered after
    // the page was opened turns up with no interaction. There is no Navigate call after
    // the initial load, which is what makes this a check of the poll and not of a reload.
    [Fact]
    public void TodayQueue_PicksUpANewCheckInWithoutAnyReload()
    {
        using var seed = new SeedClient();

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, QueueManagementPage.Path);

        var queue = new QueueManagementPage(Driver);
        queue.WaitUntilLoaded();
        var before = queue.QueueNumbersInOrder.ToList();

        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        var queueNumber = queue.WaitForPatientRow(patient.FullName);

        Assert.DoesNotContain(queueNumber, before);
        Assert.Equal(patient.FullName, queue.PatientNameFor(queueNumber));
    }

    private static int ParseQueueNumber(string queueNumber) => int.Parse(queueNumber["Q-".Length..]);
}
