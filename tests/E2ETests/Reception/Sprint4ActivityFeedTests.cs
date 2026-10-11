using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Reception;

[Trait("Category", "E2E")]
[Trait("Story", "SWC-135")]
[Collection(E2ETestCollections.SharedQueue)]
public sealed class Sprint4ActivityFeedTests : SeleniumTestBase
{
    [Fact]
    public void Receptionist_SeesNewCheckInAndCanSwitchTodayAndAllActivity()
    {
        using var seed = new SeedClient();
        var patient = seed.RegisterPatient(TestData.FullName("Sprint 4 Activity"));
        seed.WaitUntilWaiting(patient.PatientId);

        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, ActivityFeedPage.Path);

        var feed = new ActivityFeedPage(Driver);
        feed.WaitUntilLoaded();
        Assert.True(feed.TodaySelected);
        feed.WaitForText(patient.FullName);
        Assert.True(feed.ContainsText("checked in"));
        Assert.True(feed.OccurredAt.SequenceEqual(feed.OccurredAt.OrderDescending()));

        feed.SelectAll();
        Assert.True(feed.AllSelected);
        feed.WaitForText(patient.FullName);

        feed.SelectToday();
        Assert.True(feed.TodaySelected);
        feed.WaitForText(patient.FullName);
    }
}
