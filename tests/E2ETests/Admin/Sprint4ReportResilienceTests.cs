using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Admin;

[Trait("Category", "E2E")]
[Trait("Story", "SWC-135")]
public sealed class Sprint4ReportResilienceTests : SeleniumTestBase
{
    [Theory]
    [InlineData("daily", "activity")]
    [InlineData("daily", "prescriptions")]
    [InlineData("monthly", "activity")]
    [InlineData("monthly", "prescriptions")]
    public void Report_WhenOneServiceFails_KeepsTheOtherSectionAvailable(string period, string failedSection)
    {
        AppSession.LogIn(Driver, "admin.fernando");
        var failedPath = (period, failedSection) switch
        {
            ("daily", "activity") => "/api/reports/daily",
            ("daily", "prescriptions") => "/api/prescriptions/report/daily",
            ("monthly", "activity") => "/api/reports/monthly",
            ("monthly", "prescriptions") => "/api/prescriptions/report/monthly",
            _ => throw new ArgumentOutOfRangeException(),
        };
        BrowserFaults.FailRequests(Driver, failedPath, status: 503);

        if (period == "daily")
        {
            AppSession.GoTo(Driver, DailyReportPage.Path);
            var report = new DailyReportPage(Driver);
            report.WaitUntilLoaded();
            Assert.Equal(failedSection == "activity", report.ActivityUnavailable);
            Assert.Equal(failedSection == "prescriptions", report.PrescriptionsUnavailable);
            Assert.True(failedSection == "activity" ? report.HasPrescriptionData : report.HasActivityData);
        }
        else
        {
            AppSession.GoTo(Driver, MonthlyReportPage.Path);
            var report = new MonthlyReportPage(Driver);
            report.WaitUntilLoaded();
            Assert.Equal(failedSection == "activity", report.ActivityUnavailable);
            Assert.Equal(failedSection == "prescriptions", report.PrescriptionsUnavailable);
            Assert.True(failedSection == "activity" ? report.HasPrescriptionData : report.HasActivityData);
        }
    }
}
