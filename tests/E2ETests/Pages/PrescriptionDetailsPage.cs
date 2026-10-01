using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The prescription details screen reached from the receptionist's queue (SWC-30 view,
// SWC-41 dispense). Doctors and Admins open the same route read-only. Route and selectors
// mirror frontend/src/pages/PrescriptionDetailsPage.tsx.
public class PrescriptionDetailsPage
{
    public static string PathFor(string queueId) => $"/prescriptions/queue/{queueId}";

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public PrescriptionDetailsPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        _wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
    }

    public void WaitUntilLoaded() =>
        _wait.Until(d =>
            d.FindElements(By.XPath("//p[contains(normalize-space(), 'Loading prescription')]")).Count == 0
            && d.FindElements(By.CssSelector(
                "[data-testid='prescription-details'], [data-testid='prescription-load-error']")).Count > 0);

    public bool HasLoadError =>
        _driver.FindElements(By.CssSelector("[data-testid='prescription-load-error']")).Count > 0;

    // Receptionist-only header: Patient, Queue number, Doctor, Room.
    public string CounterDetail(string label) =>
        _driver.FindElement(By.XPath(
            $"//dl[@data-testid='counter-details']/div[dt[normalize-space()='{label}']]/dd")).Text.Trim();

    public bool ShowsNoPrescriptionYet =>
        _driver.FindElements(By.XPath(
            "//*[@role='status'][normalize-space()='No prescription recorded yet. Doctor may still be writing it.']")).Count > 0;

    public string Status =>
        _driver.FindElement(By.CssSelector("[data-testid='prescription-status']")).Text.Trim();

    public string PrescriptionDate =>
        _driver.FindElement(By.CssSelector("[data-testid='prescription-date']")).Text.Trim();

    // Doctor/Admin view only; the receptionist view shows the doctor in the header instead.
    public string? PrescribedByLine =>
        _driver.FindElements(By.XPath("//p[starts-with(normalize-space(), 'Prescribed by')]"))
            .FirstOrDefault()?.Text.Trim();

    public IReadOnlyList<string> MedicineNames =>
        _driver.FindElements(By.XPath("//h3[normalize-space()='Medicines']/following-sibling::ul/li/p[1]"))
            .Select(element => element.Text.Trim())
            .ToList();

    // Dosage, Frequency, Duration or Instructions for one medicine.
    public string MedicineDetail(string medicineName, string field) =>
        _driver.FindElement(By.XPath(
            $"//h3[normalize-space()='Medicines']/following-sibling::ul/li[p[1][normalize-space()='{medicineName}']]" +
            $"//div[dt[normalize-space()='{field}']]/dd")).Text.Trim();

    private static readonly By DispenseButton = By.XPath("//button[normalize-space()='Mark as Dispensed']");

    public bool HasDispenseButton => _driver.FindElements(DispenseButton).Count > 0;

    // Dispensing is irreversible, so the page asks for a second, explicit confirmation.
    public void ClickMarkAsDispensed()
    {
        _driver.FindElement(DispenseButton).Click();
        _wait.Until(d => d.FindElement(By.XPath(
            "//*[@role='alertdialog']//button[normalize-space()='Confirm Dispense']"))).Click();
    }

    // "Dispensed by <name> at <time>", shown in place of the dispense button.
    public string WaitForDispensedMessage() =>
        _wait.Until(d => d.FindElements(By.XPath("//*[@role='status'][starts-with(normalize-space(), 'Dispensed by')]"))
            .FirstOrDefault()?.Text.Trim())
        ?? throw new InvalidOperationException("The dispensed message was not shown.");

    public void Refresh()
    {
        _driver.Navigate().Refresh();
        WaitUntilLoaded();
    }
}
