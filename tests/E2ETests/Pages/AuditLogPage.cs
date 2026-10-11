using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

public sealed class AuditLogPage
{
    public const string Path = "/admin/audit-log";
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public AuditLogPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
    }

    public void WaitUntilLoaded() => _wait.Until(d =>
        d.FindElements(By.CssSelector("[data-testid='audit-log-table']")).Count > 0 ||
        d.PageSource.Contains("No activity recorded yet", StringComparison.Ordinal));

    public void WaitForEntry(string username, string action, string? targetUsername = null) =>
        _wait.Until(d => d.FindElements(By.CssSelector("[data-testid='audit-log-row']")).Any(row =>
            row.Text.Contains(username, StringComparison.Ordinal) &&
            row.Text.Contains(action, StringComparison.Ordinal) &&
            (targetUsername is null || row.Text.Contains(targetUsername, StringComparison.Ordinal))));
}
