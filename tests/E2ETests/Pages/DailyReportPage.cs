using E2ETests.Support;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

public sealed class DailyReportPage
{
    public const string Path = "/admin/reports/daily";
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public DailyReportPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
    }

    public void WaitUntilLoaded() => _wait.Until(d =>
        !d.PageSource.Contains("Loading the report", StringComparison.Ordinal) &&
        d.FindElements(By.Id("report-date")).Count == 1);

    public void SelectDate(DateOnly date)
    {
        Browser.SetInputValue(_driver, _driver.FindElement(By.Id("report-date")), date.ToString("yyyy-MM-dd"));
        WaitUntilLoaded();
    }

    public bool HasActivityData =>
        _driver.PageSource.Contains("Total patients", StringComparison.Ordinal) ||
        _driver.PageSource.Contains("No activity recorded for this date", StringComparison.Ordinal);

    public bool HasPrescriptionData =>
        _driver.PageSource.Contains("Prescriptions written", StringComparison.Ordinal) ||
        _driver.PageSource.Contains("No activity recorded for this date", StringComparison.Ordinal);

    public bool ActivityUnavailable => _driver.PageSource.Contains("Activity data currently unavailable.", StringComparison.Ordinal);
    public bool PrescriptionsUnavailable => _driver.PageSource.Contains("Prescription data currently unavailable.", StringComparison.Ordinal);
    public bool ContainsDiagnosis(string diagnosis) => _driver.PageSource.Contains(diagnosis, StringComparison.Ordinal);

    public void WaitForDiagnosis(string diagnosis, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(25));
        while (DateTime.UtcNow < deadline)
        {
            WaitUntilLoaded();
            if (ContainsDiagnosis(diagnosis)) return;
            Thread.Sleep(750);
            _driver.Navigate().Refresh();
        }

        throw new WebDriverTimeoutException($"Diagnosis '{diagnosis}' did not reach the daily report before the timeout.");
    }
}
