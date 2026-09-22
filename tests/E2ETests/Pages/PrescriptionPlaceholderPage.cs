using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The doctor-facing placeholder ConsultationPage.tsx navigates to once a consultation is
// completed (SWC-26/SWC-38). Full prescription entry belongs to SWC-29; this page object
// only proves the navigation itself and the completed-consultation context it carries.
public class PrescriptionPlaceholderPage
{
    public const string Path = "/doctor/prescription";

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public PrescriptionPlaceholderPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
    }

    public void WaitUntilLoaded() =>
        _wait.Until(d => d.FindElements(By.XPath("//h1[normalize-space()='Prescription']")).Count > 0);

    public bool ShowsCompletedConfirmation =>
        _driver.FindElements(By.XPath(
            "//*[normalize-space()='Consultation completed successfully.']")).Count > 0;
}
