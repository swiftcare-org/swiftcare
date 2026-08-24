using E2ETests.Config;
using E2ETests.Pages;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Auth;

// Covers SWC-7 (User Logout) acceptance criteria. See LoginTests for run
// prerequisites and why this is tagged Category=E2E.
[Trait("Category", "E2E")]
public class LogoutTests : SeleniumTestBase
{
    // Scenario 1 - Successful logout.
    [Fact]
    public void Logout_FromDashboard_ClearsTokenAndRedirectsToLogin()
    {
        var loginPage = new LoginPage(Driver);
        loginPage.NavigateTo();
        loginPage.SubmitCredentials("dr.chen", TestConfig.SeedPassword);
        loginPage.WaitForRedirectAwayFromLogin();

        var dashboardPage = new DashboardPage(Driver);
        dashboardPage.WaitUntilLoaded();
        dashboardPage.SignOut();

        Assert.Contains("/login", Driver.Url);
        Assert.Null(GetStoredToken());
    }

    // Scenario 2 - Access after logout: a protected route redirects back to
    // login instead of rendering any data.
    [Fact]
    public void ProtectedRoute_AfterLogout_RedirectsToLogin()
    {
        var loginPage = new LoginPage(Driver);
        loginPage.NavigateTo();
        loginPage.SubmitCredentials("dr.chen", TestConfig.SeedPassword);
        loginPage.WaitForRedirectAwayFromLogin();

        var dashboardPage = new DashboardPage(Driver);
        dashboardPage.WaitUntilLoaded();
        dashboardPage.SignOut();

        Driver.Navigate().GoToUrl($"{TestConfig.BaseUrl}/doctor");

        // ProtectedRoute redirects client-side after mount, so give React
        // Router a beat rather than asserting on the pre-redirect URL.
        new WebDriverWait(Driver, TimeSpan.FromSeconds(5)).Until(d => d.Url.Contains("/login"));
        Assert.Contains("/login", Driver.Url);
    }
}
