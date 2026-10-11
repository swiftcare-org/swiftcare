using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

public sealed class ActivityFeedPage
{
    public const string Path = "/activity";
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public ActivityFeedPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
        _wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
    }

    private IWebElement TodayButton => _driver.FindElement(By.XPath("//button[normalize-space()='Today']"));
    private IWebElement AllButton => _driver.FindElement(By.XPath("//button[normalize-space()='All activity']"));

    public void WaitUntilLoaded() => _wait.Until(d =>
        d.FindElements(By.CssSelector("[data-testid='activity-feed']")).Count > 0 ||
        d.PageSource.Contains("No activity recorded", StringComparison.Ordinal));

    public bool TodaySelected => TodayButton.GetDomAttribute("aria-pressed") == "true";
    public bool AllSelected => AllButton.GetDomAttribute("aria-pressed") == "true";

    public void SelectToday()
    {
        TodayButton.Click();
        _wait.Until(_ => TodaySelected);
        WaitUntilLoaded();
    }

    public void SelectAll()
    {
        AllButton.Click();
        _wait.Until(_ => AllSelected);
        WaitUntilLoaded();
    }

    public void WaitForText(string text, TimeSpan? timeout = null) =>
        new WebDriverWait(_driver, timeout ?? TimeSpan.FromSeconds(30))
            .Until(d => d.FindElements(By.CssSelector("[data-testid='activity-entry']"))
                .Any(entry => entry.Text.Contains(text, StringComparison.Ordinal)));

    public void WaitForEntryContainingAll(TimeSpan timeout, params string[] text) =>
        new WebDriverWait(_driver, timeout)
            .Until(d => d.FindElements(By.CssSelector("[data-testid='activity-entry']"))
                .Any(entry => text.All(part => entry.Text.Contains(part, StringComparison.Ordinal))));

    public bool ContainsText(string text) =>
        _driver.FindElements(By.CssSelector("[data-testid='activity-entry']"))
            .Any(entry => entry.Text.Contains(text, StringComparison.Ordinal));

    public IReadOnlyList<DateTimeOffset> OccurredAt =>
        _driver.FindElements(By.CssSelector("[data-testid='activity-entry'] time"))
            .Select(element => DateTimeOffset.Parse(element.GetDomAttribute("datetime")!)).ToArray();
}
