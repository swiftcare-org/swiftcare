using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

public class DashboardPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public DashboardPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
    }

    private IWebElement SignOutButton =>
        _driver.FindElement(By.XPath("//button[normalize-space()='Sign Out']"));

    public void WaitUntilLoaded()
    {
        _wait.Until(d => d.FindElements(By.XPath("//button[normalize-space()='Sign Out']")).Count > 0);
    }

    // The button lives in the sidebar, which every page renders afresh, so the click is
    // retried if the button is replaced between being found and being clicked.
    public void SignOut()
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(5));
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
        wait.Until(_ =>
        {
            SignOutButton.Click();
            return true;
        });
        _wait.Until(d => d.Url.Contains("/login"));
    }
}
