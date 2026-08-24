using E2ETests.Drivers;
using OpenQA.Selenium;

namespace E2ETests;

// One browser session per test (xUnit creates a new class instance per test
// method), so sessionStorage - and therefore login state - never bleeds
// between tests.
public abstract class SeleniumTestBase : IDisposable
{
    protected IWebDriver Driver { get; }

    protected SeleniumTestBase()
    {
        Driver = DriverFactory.CreateChromeDriver();
    }

    protected string? GetStoredToken() =>
        (string?)((IJavaScriptExecutor)Driver).ExecuteScript(
            "return sessionStorage.getItem('swiftcare.auth.token');");

    public void Dispose()
    {
        Driver.Quit();
        Driver.Dispose();
        GC.SuppressFinalize(this);
    }
}
