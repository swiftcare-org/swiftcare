using E2ETests.Pages;
using E2ETests.Support;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Auth;

[Trait("Category", "E2E")]
[Trait("Story", "SWC-135")]
public sealed class Sprint4RoleRestrictionTests : SeleniumTestBase
{
    [Theory]
    [InlineData("reception.silva", DailyReportPage.Path, "/reception")]
    [InlineData("reception.silva", MonthlyReportPage.Path, "/reception")]
    [InlineData("dr.chen", DailyReportPage.Path, "/doctor")]
    [InlineData("dr.chen", MonthlyReportPage.Path, "/doctor")]
    public void NonAdmin_CannotOpenAdminReports(string username, string restrictedPath, string expectedPath)
    {
        AppSession.LogIn(Driver, username);
        AppSession.GoTo(Driver, restrictedPath);

        new WebDriverWait(Driver, TimeSpan.FromSeconds(10))
            .Until(d => new Uri(d.Url).AbsolutePath == expectedPath);

        Assert.Equal(expectedPath, new Uri(Driver.Url).AbsolutePath);
    }
}
