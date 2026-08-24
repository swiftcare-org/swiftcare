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

    public void SignOut()
    {
        SignOutButton.Click();
        _wait.Until(d => d.Url.Contains("/login"));
    }
}
