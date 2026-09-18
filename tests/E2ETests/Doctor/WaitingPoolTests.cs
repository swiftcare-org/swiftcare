using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Doctor;

// Covers SWC-21 (Doctor View Shared Waiting Pool) in the browser. The payload contract,
// the ordering and the role gating are covered by
// docs/testing/postman/SWC-21-collection.json. These tests cover the two things the API
// checks cannot reach: that every doctor is shown the same unfiltered pool with the name
// resolved live from PatientService, and that a patient called by somebody else leaves an
// already-open pool on its own poll. The second is the criterion QA could only confirm at
// the unit level when SWC-21 shipped, because no endpoint moved a patient out of WAITING
// until SWC-22 landed - see docs/testing/SWC-21-test-results.md, AC2.
//
// AC3's empty pool is deliberately not automated. Reaching it means deleting today's queue,
// including records not owned by this test run. The suite only deletes rows for patients it
// created, so the empty state stays covered by
// GetWaitingWhenNoPatientsAreWaitingReturnsEmptyCollection and by the stubbed-network
// browser pass recorded in SWC-21's results, TC-07 and TC-08.
[Trait("Category", "E2E")]
[Collection(E2ETestCollections.SharedQueue)]
public class WaitingPoolTests : SeleniumTestBase
{
    // AC1 - the pool is shared: two doctors with different rooms, neither of them connected
    // to this patient in any way, are shown the same waiting entry with the three columns
    // the criterion names, in queue-number order.
    [Fact]
    public void WaitingPool_ShowsTheSameEntryToEveryDoctor_WithQueueNumberNameAndCheckInTime()
    {
        using var seed = new SeedClient();
        var doctorA = seed.CreateUser("Doctor");
        var doctorB = seed.CreateUser("Doctor");
        var patient = seed.RegisterPatient();
        seed.WaitUntilWaiting(patient.PatientId);

        var poolAsDoctorA = OpenWaitingPoolAs(doctorA);

        // The pool's table only renders once at least one entry exists (DoctorDashboard.tsx
        // shows the empty-state placeholder instead until then), so the row has to be waited
        // for before the header assertion can rely on a <table> being on the page at all.
        var queueNumber = poolAsDoctorA.WaitForPatientRow(patient.FullName);
        Assert.Equal(new[] { "Queue Number", "Patient", "Check-in Time" }, poolAsDoctorA.ColumnHeadings);
        Assert.NotEmpty(poolAsDoctorA.CheckInTimeFor(queueNumber));

        var numbers = poolAsDoctorA.WaitingQueueNumbersInOrder.Select(ParseQueueNumber).ToList();
        Assert.Equal(numbers.OrderBy(value => value), numbers);

        // The same patient, from a second doctor's session in a different room. Asserting on
        // this one entry rather than on the whole set, because the pool is shared clinic-wide
        // and another test's doctor may legitimately call someone away between the two reads.
        var poolAsDoctorB = OpenWaitingPoolAs(doctorB);
        Assert.Equal(queueNumber, poolAsDoctorB.WaitForPatientRow(patient.FullName));
        Assert.Equal(patient.FullName, poolAsDoctorB.PatientNameFor(queueNumber));
    }

    // AC2 - the pool refreshes itself every five seconds: a patient checked in after the
    // page loaded appears, and a patient called by a different doctor disappears. There is
    // no Navigate call after the initial load, so the only way this passes is the page's own
    // poll.
    [Fact]
    public void WaitingPool_AddsNewCheckInsAndDropsPatientsCalledByOtherDoctors_WithoutReload()
    {
        using var seed = new SeedClient();
        var watchingDoctor = seed.CreateUser("Doctor");
        var callingDoctor = seed.CreateUser("Doctor");

        var pool = OpenWaitingPoolAs(watchingDoctor);

        var arrival = seed.RegisterPatient();
        seed.WaitUntilWaiting(arrival.PatientId);
        var arrivalQueueNumber = pool.WaitForPatientRow(arrival.FullName);

        // Whoever is first in line is who the other doctor's call will take, and on a shared
        // local queue that is not necessarily the patient registered above. The rendered set
        // is captured before the call rather than after it, so the "was on screen" assertion
        // cannot race the very poll the next line is waiting on.
        var renderedBeforeTheCall = pool.WaitingQueueNumbersInOrder.ToList();
        var called = seed.CallNext(callingDoctor.Username, callingDoctor.Password);
        Assert.Contains(called.QueueNumber, renderedBeforeTheCall);

        // Ten seconds covers one full 5-second poll cycle plus margin for the request.
        pool.WaitForWaitingRowGone(called.QueueNumber, TimeSpan.FromSeconds(10));

        // The arrival is still there unless it was the one called, which proves the drop was
        // the called entry leaving rather than the table being emptied or re-rendered blank.
        if (called.QueueNumber != arrivalQueueNumber)
        {
            Assert.Contains(arrivalQueueNumber, pool.WaitingQueueNumbersInOrder);
        }
    }

    private DoctorDashboardPage OpenWaitingPoolAs(SeededUser doctor)
    {
        var login = new LoginPage(Driver);
        login.NavigateTo();
        login.SubmitCredentials(doctor.Username, doctor.Password);
        login.WaitForRedirectAwayFromLogin();

        var dashboard = new DoctorDashboardPage(Driver);
        dashboard.WaitUntilLoaded();
        return dashboard;
    }

    private static int ParseQueueNumber(string queueNumber) => int.Parse(queueNumber["Q-".Length..]);
}
