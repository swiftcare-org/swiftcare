using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The "Patient Profile" screen (SWC-17: manage allergies). Route is
// /patients/{id}; selectors mirror frontend/src/pages/PatientProfilePage.tsx.
//
// Note: severity text in the table is rendered upper-case by CSS
// (text-transform), so IWebElement.Text returns e.g. "SEVERE". Compare
// case-insensitively.
public class PatientProfilePage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public PatientProfilePage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
    }

    public void WaitUntilLoaded() =>
        _wait.Until(d => d.FindElements(By.XPath("//p[normalize-space()='Allergies']")).Count > 0);

    // --- Red allergy-alert banner (rendered only when at least one allergy exists) ---

    public bool HasAllergyAlert => _driver.FindElements(AlertBanner).Count > 0;

    public void WaitForAllergyAlertContaining(string text) =>
        _wait.Until(d =>
        {
            var banner = d.FindElements(AlertBanner);
            return banner.Count > 0 && banner[0].Text.Contains(text);
        });

    public void WaitForNoAllergyAlert() => _wait.Until(d => d.FindElements(AlertBanner).Count == 0);

    // --- Allergies table ---

    public IReadOnlyList<string> AllergyNamesInOrder => ColumnText("td:first-child");

    public IReadOnlyList<string> AllergySeveritiesInOrder => ColumnText("td:nth-child(2)");

    public bool ShowsNoAllergiesRecorded =>
        _driver.FindElements(By.XPath("//*[normalize-space()='No allergies recorded']")).Count > 0;

    public void WaitForAllergyRow(string name) => _wait.Until(d => d.FindElements(RowCell(name)).Count > 0);

    public void WaitForAllergyRowGone(string name) => _wait.Until(d => d.FindElements(RowCell(name)).Count == 0);

    // --- Add allergy (Doctor / Receptionist only) ---

    public void AddAllergy(string name, string severity, string? notes = null)
    {
        var nameInput = _driver.FindElement(By.Id("add-allergy-name"));
        nameInput.Clear();
        nameInput.SendKeys(name);
        new SelectElement(_driver.FindElement(By.Id("add-severity"))).SelectByValue(severity);

        if (notes is not null)
        {
            var notesInput = _driver.FindElement(By.Id("add-notes"));
            notesInput.Clear();
            notesInput.SendKeys(notes);
        }

        _driver.FindElement(By.XPath("//button[normalize-space()='Add Allergy']")).Click();
    }

    public void SubmitAddAllergyForm() =>
        _driver.FindElement(By.XPath("//button[normalize-space()='Add Allergy']")).Click();

    public string? AddAllergyNameError
    {
        get
        {
            var elements = _driver.FindElements(By.Id("add-allergy-name-error"));
            return elements.Count > 0 ? elements[0].Text : null;
        }
    }

    // --- Edit / remove (one inline form open at a time) ---

    public void StartEdit(string currentName) => RowActionButton(currentName, "Edit").Click();

    public void SubmitEdit(string newName, string? severity = null)
    {
        var nameInput = _wait.Until(d => d.FindElement(By.CssSelector("input[id^='edit-name-']")));
        nameInput.Clear();
        nameInput.SendKeys(newName);

        if (severity is not null)
        {
            new SelectElement(_driver.FindElement(By.CssSelector("select[id^='edit-severity-']"))).SelectByValue(severity);
        }

        _driver.FindElement(By.XPath("//button[normalize-space()='Save']")).Click();
    }

    public void StartRemove(string name) => RowActionButton(name, "Remove").Click();

    public void ConfirmRemove() => _driver.FindElement(By.XPath("//button[normalize-space()='Confirm']")).Click();

    private IReadOnlyList<string> ColumnText(string cellSelector) =>
        _driver.FindElements(By.CssSelector($"table tbody tr {cellSelector}"))
            .Select(e => e.Text.Trim())
            .Where(t => t.Length > 0)
            .ToList();

    private IWebElement RowActionButton(string allergyName, string buttonText) =>
        _driver.FindElement(By.XPath(
            $"//tr[td[normalize-space()='{allergyName}']]//button[normalize-space()='{buttonText}']"));

    private static By AlertBanner => By.CssSelector("div[role='alert']");

    private static By RowCell(string name) => By.XPath($"//table//td[normalize-space()='{name}']");
}
