using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

public sealed class MonthlyReportPage
{
    public const string Path = "/admin/reports/monthly";
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public MonthlyReportPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
    }

    public void WaitUntilLoaded() => _wait.Until(d =>
        !d.PageSource.Contains("Loading the report", StringComparison.Ordinal) &&
        d.FindElements(By.Id("report-month")).Count == 1);

    public bool HasActivityData =>
        _driver.PageSource.Contains("Total patients", StringComparison.Ordinal) ||
        _driver.PageSource.Contains("No activity recorded for this month", StringComparison.Ordinal);

    public bool HasPrescriptionData =>
        _driver.PageSource.Contains("Prescriptions written", StringComparison.Ordinal) ||
        _driver.PageSource.Contains("No activity recorded for this month", StringComparison.Ordinal);

    public bool ActivityUnavailable => _driver.PageSource.Contains("Activity data currently unavailable.", StringComparison.Ordinal);
    public bool PrescriptionsUnavailable => _driver.PageSource.Contains("Prescription data currently unavailable.", StringComparison.Ordinal);
}
