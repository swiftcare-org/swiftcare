using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

public sealed class PatientHistoryPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public PatientHistoryPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
        _wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
    }

    public static string Path(string patientId) => $"/patients/{patientId}/history";

    public void WaitUntilLoaded() => _wait.Until(d =>
        d.FindElements(By.Id("consultation-history-heading")).Count == 1 &&
        d.FindElements(By.Id("vitals-history-heading")).Count == 1 &&
        d.FindElements(By.Id("prescription-history-heading")).Count == 1 &&
        !d.PageSource.Contains("Loading consultations", StringComparison.Ordinal) &&
        !d.PageSource.Contains("Loading vital signs", StringComparison.Ordinal) &&
        !d.PageSource.Contains("Loading prescriptions", StringComparison.Ordinal));

    public void ExpandConsultationContaining(string text)
    {
        var button = _wait.Until(d => d.FindElement(By.XPath(
            $"//section[.//*[@id='consultation-history-heading']]//button[contains(., '{text}')]")));
        if (button.GetDomAttribute("aria-expanded") != "true") button.Click();
        _wait.Until(_ => button.GetDomAttribute("aria-expanded") == "true");
    }

    public bool ConsultationDetailsContain(string text) =>
        _driver.FindElements(By.CssSelector("[id^='consultation-details-']"))
            .Any(details => details.Text.Contains(text, StringComparison.Ordinal));

    public bool VitalsContain(string text) =>
        _driver.FindElement(By.XPath("//section[.//*[@id='vitals-history-heading']]")).Text
            .Contains(text, StringComparison.Ordinal);

    public bool PrescriptionHistoryContains(string text) =>
        _driver.FindElement(By.XPath("//section[.//*[@id='prescription-history-heading']]")).Text
            .Contains(text, StringComparison.Ordinal);
}
