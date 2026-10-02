using E2ETests.Config;
using E2ETests.Pages;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Support;

// Shared browser-session helpers: log in as one of the development-seeded
// accounts and land on a fully rendered dashboard, then navigate to a route.
public static class AppSession
{
    public static void LogIn(IWebDriver driver, string username, string? password = null)
    {
        var login = new LoginPage(driver);
        login.NavigateTo();
        login.SubmitCredentials(username, password ?? TestConfig.SeedPassword);
        login.WaitForRedirectAwayFromLogin();

        // Wait for the dashboard shell so callers can act on links/content immediately.
        new WebDriverWait(driver, TimeSpan.FromSeconds(10))
            .Until(d => d.FindElements(By.XPath("//button[normalize-space()='Sign Out']")).Count > 0);
    }

    public static void GoTo(IWebDriver driver, string path)
    {
        driver.Navigate().GoToUrl($"{TestConfig.BaseUrl}{path}");
    }

    // Clicks a dashboard navigation link by its target path. Matching on href
    // rather than link text, because the dashboard links are upper-cased by CSS
    // and Selenium's By.LinkText matches the rendered (transformed) text.
    //
    // Finding and clicking happen inside one retried step: every page renders its own
    // sidebar, so a link found just before a route change is replaced by the time it is
    // clicked. Waiting for the address afterwards keeps two clicks in a row from overlapping.
    public static void ClickNavLink(IWebDriver driver, string href)
    {
        var selector = By.CssSelector($"a[href='{href}']");
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));

        wait.Until(d =>
        {
            d.FindElement(selector).Click();
            return true;
        });
        wait.Until(d => new Uri(d.Url).AbsolutePath == href);
    }
}
