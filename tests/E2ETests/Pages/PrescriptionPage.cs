using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The doctor's Prescription screen (SWC-29 create, SWC-40 add/remove), reached from
// Complete Consultation. Route and selectors mirror frontend/src/pages/PrescriptionPage.tsx.
//
// The medicine inputs have no ids, so each is found through its wrapping label. Allergy
// banners also use role="alert", so form messages are always scoped to their own form.
public class PrescriptionPage
{
    public const string Path = "/doctor/prescription";

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public PrescriptionPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        _wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
    }

    public void WaitUntilLoaded() =>
        _wait.Until(d => d.FindElements(By.XPath("//h1[normalize-space()='Prescription']")).Count > 0);

    public bool ShowsCompletedConfirmation =>
        _driver.FindElements(By.XPath(
            "//*[normalize-space()='Consultation completed successfully.']")).Count > 0;

    // The context recovery and the allergy/history requests both finish after the heading
    // renders; wait for both before reading the form or the saved prescription.
    public void WaitUntilReady()
    {
        WaitUntilLoaded();
        _wait.Until(d =>
            d.FindElements(By.XPath("//p[contains(normalize-space(), 'Recovering your pending prescription')]")).Count == 0
            && d.FindElements(By.XPath("//p[contains(normalize-space(), 'Loading prescription history')]")).Count == 0);
    }

    public void Refresh()
    {
        _driver.Navigate().Refresh();
        WaitUntilReady();
    }

    // --- Allergy warnings (SWC-29 AC2) ---

    public IReadOnlyList<string> AllergyWarningTexts =>
        _driver.FindElements(By.CssSelector("section[aria-label='Allergy warnings'] [role='alert']"))
            .Select(element => (element.GetDomProperty("textContent") ?? string.Empty).Trim())
            .ToList();

    // --- Unsaved prescription form (SWC-29) ---

    private static readonly By DraftForm = By.XPath("//form[.//button[@type='submit'][normalize-space()='Save Prescription']]");

    private static readonly By HeaderAddMedicineButton =
        By.XPath("//button[@type='button'][normalize-space()='Add Medicine']");

    public bool HasAddMedicineButton => _driver.FindElements(HeaderAddMedicineButton).Count > 0;

    public void ClickAddMedicine() => _wait.Until(d => d.FindElement(HeaderAddMedicineButton)).Click();

    public int DraftMedicineCount => _driver.FindElements(By.XPath("//form//fieldset")).Count;

    public void FillDraftMedicine(
        int position,
        string name,
        string dosage,
        string frequency,
        string duration,
        string? instructions = null)
    {
        var row = _wait.Until(d => d.FindElement(DraftRow(position)));
        FillMedicineFields(row, name, dosage, frequency, duration, instructions);
    }

    public void ClickRemoveDraftMedicine(int position) =>
        _wait.Until(d => d.FindElement(DraftRow(position)))
            .FindElement(By.XPath(".//button[normalize-space()='Remove']")).Click();

    public void ClickSavePrescription() =>
        _driver.FindElement(DraftForm).FindElement(By.XPath(".//button[@type='submit']")).Click();

    public string WaitForDraftMessage() =>
        _wait.Until(d => d.FindElements(By.XPath("//form//p[@role='alert']")).FirstOrDefault()?.Text.Trim())
        ?? throw new InvalidOperationException("No prescription form message was shown.");

    private static By DraftRow(int position) =>
        By.XPath($"//form//fieldset[.//legend[normalize-space()='Medicine {position}']]");

    // --- Removal confirmation (SWC-40 AC2), shared by draft rows and saved medicines ---

    private static readonly By RemovalDialog = By.CssSelector("[role='alertdialog']");

    public string WaitForRemovalPrompt() =>
        _wait.Until(d => d.FindElement(RemovalDialog)).FindElement(By.TagName("p")).Text.Trim();

    public void ConfirmRemoval() =>
        _driver.FindElement(RemovalDialog).FindElement(By.XPath(".//button[normalize-space()='Confirm Remove']")).Click();

    public void CancelRemoval() =>
        _driver.FindElement(RemovalDialog).FindElement(By.XPath(".//button[normalize-space()='Cancel']")).Click();

    public bool HasRemovalPrompt => _driver.FindElements(RemovalDialog).Count > 0;

    // --- Saved prescription (SWC-29 save, SWC-40 add/remove/read-only) ---

    private static readonly By SavedMedicineList = By.CssSelector("ul[aria-label='Prescription medicines']");

    public void WaitForSavedPrescription() => _wait.Until(d => d.FindElements(SavedMedicineList).Count > 0);

    public IReadOnlyList<string> SavedMedicineNames =>
        _driver.FindElements(By.CssSelector("ul[aria-label='Prescription medicines'] > li p.font-semibold"))
            .Select(element => element.Text.Trim())
            .ToList();

    public void WaitForSavedMedicineNames(params string[] names) =>
        _wait.Until(_ => SavedMedicineNames.SequenceEqual(names));

    public bool HasRemoveButtons =>
        _driver.FindElements(By.XPath("//ul[@aria-label='Prescription medicines']//button[normalize-space()='Remove']")).Count > 0;

    public void ClickRemoveSavedMedicine(string medicineName) =>
        _wait.Until(d => d.FindElement(By.XPath(
            $"//ul[@aria-label='Prescription medicines']/li[.//p[normalize-space()='{medicineName}']]//button[normalize-space()='Remove']")))
            .Click();

    private static readonly By AdditionForm = By.XPath("//form[.//h3[normalize-space()='Add another medicine']]");

    public void AddSavedMedicine(
        string name,
        string dosage,
        string frequency,
        string duration,
        string? instructions = null)
    {
        ClickAddMedicine();
        var form = _wait.Until(d => d.FindElement(AdditionForm));
        FillMedicineFields(form, name, dosage, frequency, duration, instructions);
        form.FindElement(By.XPath(".//button[@type='submit']")).Click();
    }

    // Success and error messages for the saved prescription share one line, rendered with
    // role status or alert depending on the outcome.
    public void WaitForMessage(string text) =>
        _wait.Until(d => d.FindElements(By.XPath(
            $"//*[@role='status' or @role='alert'][normalize-space()='{text}']")).Count > 0);

    public bool ShowsDispensedNotice =>
        _driver.FindElements(By.XPath(
            "//p[@role='status'][normalize-space()='Cannot modify a dispensed prescription']")).Count > 0;

    public bool HasBackToDashboardLink =>
        _driver.FindElements(By.CssSelector("a[href='/doctor']")).Count > 0;

    // --- Previous prescriptions (SWC-29 AC4) ---

    public bool ShowsNoPreviousPrescriptions =>
        _driver.FindElements(By.XPath("//p[normalize-space()='No previous prescriptions found.']")).Count > 0;

    private IWebElement LatestHistoryEntry =>
        _wait.Until(d => d.FindElement(By.XPath(
            "//section[.//h2[normalize-space()='Previous prescriptions']]//article[1]")));

    public string LatestHistoryStatus => LatestHistoryEntry.FindElement(By.CssSelector("p.font-bold")).Text.Trim();

    public string LatestHistoryDoctorLine =>
        LatestHistoryEntry.FindElement(By.XPath(".//p[starts-with(normalize-space(), 'Prescribed by')]")).Text.Trim();

    public IReadOnlyList<string> LatestHistoryMedicineNames =>
        LatestHistoryEntry.FindElements(By.CssSelector("ul li span.font-semibold"))
            .Select(element => element.Text.Trim())
            .ToList();

    private static void FillMedicineFields(
        IWebElement container,
        string name,
        string dosage,
        string frequency,
        string duration,
        string? instructions)
    {
        Field(container, "Medicine name", "input").SendKeys(name);
        Field(container, "Dosage", "input").SendKeys(dosage);
        Field(container, "Frequency", "input").SendKeys(frequency);
        Field(container, "Duration", "input").SendKeys(duration);
        if (instructions is not null)
        {
            Field(container, "Instructions", "textarea").SendKeys(instructions);
        }
    }

    private static IWebElement Field(IWebElement container, string label, string tag) =>
        container.FindElement(By.XPath($".//label[starts-with(normalize-space(), '{label}')]//{tag}"));
}
