using E2ETests.Config;
using E2ETests.Pages;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Support;

// Shared browser-session helpers: log in as one of the development-seeded
// accounts and land on a fully rendered dashboard, then navigate to a route.
public static class AppSession
{
    public static void LogIn(IWebDriver driver, string username)
    {
        var login = new LoginPage(driver);
        login.NavigateTo();
        login.SubmitCredentials(username, TestConfig.SeedPassword);
        login.WaitForRedirectAwayFromLogin();

        // Wait for the dashboard shell so callers can act on links/content immediately.
        new WebDriverWait(driver, TimeSpan.FromSeconds(10))
            .Until(d => d.FindElements(By.XPath("//button[normalize-space()='Sign Out']")).Count > 0);
    }

    public static void GoTo(IWebDriver driver, string path)
    {
        driver.Navigate().GoToUrl($"{TestConfig.BaseUrl}{path}");
    }
}
