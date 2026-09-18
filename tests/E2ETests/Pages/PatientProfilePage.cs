using System.Text.RegularExpressions;
using E2ETests.Support;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The "Patient Profile" screen (SWC-17: manage allergies; SWC-18: manage chronic
// conditions). Route is /patients/{id}; selectors mirror
// frontend/src/pages/PatientProfilePage.tsx.
//
// Note: severity text in the table is rendered upper-case by CSS
// (text-transform), so IWebElement.Text returns e.g. "SEVERE". Compare
// case-insensitively.
//
// Note: the page now renders two <table> elements (Allergies, then Chronic
// Conditions) and, for a Doctor, two role="alert" banners (the allergy banner
// and the amber chronic-condition banner). Every chronic-condition selector
// below is scoped past the "Chronic Conditions" heading or by its distinct
// banner text, on purpose - an unscoped "table tbody tr td:first-child" or
// "div[role='alert']" query would silently mix rows or banners from the two
// sections once a patient has both an allergy and a chronic condition.
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

    // --- Demographics and access mode (SWC-92) ---

    public string PatientId => DemographicValue("Patient ID");

    public string Nic => DemographicValue("NIC");

    public string PhoneNumber => DemographicValue("Phone");

    public string BloodGroup => DemographicValue("Blood Group");

    public string Address => DemographicValue("Address");

    public bool HasAllergiesSection =>
        _driver.FindElements(By.XPath("//p[normalize-space()='Allergies']")).Count > 0;

    public bool HasChronicConditionsSection =>
        _driver.FindElements(By.XPath("//p[normalize-space()='Chronic Conditions']")).Count > 0;

    // Doctors retain the existing read access from SWC-12. The demographic edit button
    // and its form are Receptionist-only; allergy controls are deliberately not included
    // here because Doctors may manage allergies under the separate SWC-17 rules.
    public bool HasEditProfileButton =>
        _driver.FindElements(By.XPath("//button[normalize-space()='Edit Profile']")).Count > 0;

    public bool HasProfileEditForm =>
        _driver.FindElements(By.Id("profile-address")).Count > 0;

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

    // --- Amber chronic-condition alert banner (Doctor only, rendered when at least one
    // active condition exists). Scoped by its own label text, not just role="alert", since
    // the red allergy banner uses the same role and can be present on the same page. ---

    public bool HasChronicConditionAlert => _driver.FindElements(ChronicConditionAlertBanner).Count > 0;

    public void WaitForChronicConditionAlertContaining(string text) =>
        _wait.Until(d =>
        {
            var banner = d.FindElements(ChronicConditionAlertBanner);
            return banner.Count > 0 && banner[0].Text.Contains(text);
        });

    public void WaitForNoChronicConditionAlert() => _wait.Until(d => d.FindElements(ChronicConditionAlertBanner).Count == 0);

    // --- Chronic Conditions table ---

    public IReadOnlyList<string> ConditionNamesInOrder => ConditionColumnText(1);

    public IReadOnlyList<string> ConditionDiagnosedDatesInOrder => ConditionColumnText(2);

    public bool ShowsNoChronicConditionsRecorded =>
        _driver.FindElements(By.XPath("//*[normalize-space()='No chronic conditions recorded']")).Count > 0;

    public void WaitForConditionRow(string name) => _wait.Until(d => d.FindElements(RowCell(name)).Count > 0);

    public void WaitForConditionRowGone(string name) => _wait.Until(d => d.FindElements(RowCell(name)).Count == 0);

    public bool HasConditionRemoveButton(string name) =>
        _driver.FindElements(By.XPath(
            $"//tr[td[normalize-space()='{name}']]//button[normalize-space()='Remove']")).Count > 0;

    // --- Add Condition (Receptionist only) ---

    public bool HasAddConditionForm => _driver.FindElements(By.Id("condition-name")).Count > 0;

    // dateDiagnosed defaults to clinic-local (Asia/Colombo) today when omitted - the date
    // field is required, so leaving it unset blocks the client-side validation and the
    // submit never fires a request at all.
    public void AddCondition(string name, string? notes = null, string? dateDiagnosed = null)
    {
        var nameInput = _driver.FindElement(By.Id("condition-name"));
        nameInput.Clear();
        nameInput.SendKeys(name);

        SetConditionDateDiagnosed(dateDiagnosed ?? ClinicTodayIsoDate());

        if (notes is not null)
        {
            var notesInput = _driver.FindElement(By.Id("condition-notes"));
            notesInput.Clear();
            notesInput.SendKeys(notes);
        }

        SubmitAddConditionForm();
    }

    public void EnterConditionName(string name)
    {
        var nameInput = _driver.FindElement(By.Id("condition-name"));
        nameInput.Clear();
        nameInput.SendKeys(name);
    }

    public void SubmitAddConditionForm() =>
        _driver.FindElement(By.XPath("//button[normalize-space()='Add Condition']")).Click();

    // Bypasses the native date input's own keyboard-entry quirks by setting the value
    // through React's tracked native setter, matching how a real date-picker selection
    // reaches React's onChange. isoDate is "yyyy-MM-dd".
    public void SetConditionDateDiagnosed(string isoDate)
    {
        const string script =
            "const el = document.getElementById('condition-date-diagnosed');" +
            "const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;" +
            "setter.call(el, arguments[0]);" +
            "el.dispatchEvent(new Event('input', { bubbles: true }));";
        ((IJavaScriptExecutor)_driver).ExecuteScript(script, isoDate);
    }

    public string? AddConditionDateError
    {
        get
        {
            var elements = _driver.FindElements(By.Id("condition-date-error"));
            return elements.Count > 0 ? elements[0].Text : null;
        }
    }

    public string? AddConditionNameError
    {
        get
        {
            var elements = _driver.FindElements(By.Id("condition-name-error"));
            return elements.Count > 0 ? elements[0].Text : null;
        }
    }

    // --- Check In (SWC-15, Receptionist only) ---
    //
    // The whole block lives inside one aria-live region that renders exactly one of:
    // the Check In button (patient not in today's queue), the green success banner
    // (this session just checked them in) or the amber already-checked-in banner. Which
    // one is on screen is the assertion, so each has its own accessor rather than one
    // shared "status text" getter.

    public bool HasCheckInButton => _driver.FindElements(CheckInButton).Count > 0;

    public void WaitForCheckInButton() => _wait.Until(d => d.FindElements(CheckInButton).Count > 0);

    public void ClickCheckIn() => _driver.FindElement(CheckInButton).Click();

    public void WaitForCheckInButtonGone() => _wait.Until(d => d.FindElements(CheckInButton).Count == 0);

    // AC1's success banner: "<full name> checked in. Queue: Q-xxx". Waits up to 25 seconds
    // because check-in is asynchronous end to end - the button POSTs, then the page polls
    // GET /api/queue/today/patient/{id} until the Kafka consumer has allocated the number
    // (PatientProfilePage.tsx, the bounded post-check-in poll) - so the default 10-second
    // page-object wait is not enough margin on a cold local stack.
    public string WaitForCheckInSuccessBanner()
    {
        var banner = new WebDriverWait(_driver, TimeSpan.FromSeconds(25))
            .Until(d =>
            {
                var elements = d.FindElements(CheckInSuccessBanner);
                return elements.Count > 0 ? elements[0] : null;
            });
        return banner!.Text;
    }

    // AC2's banner. The shipped string puts an em-dash between "in" and "Queue", which this
    // repo's own house rule forbids us from typing in source, so the locator matches on the
    // two halves either side of it rather than reproducing the character.
    public string WaitForAlreadyCheckedInBanner()
    {
        var banner = new WebDriverWait(_driver, TimeSpan.FromSeconds(15))
            .Until(d =>
            {
                var elements = d.FindElements(AlreadyCheckedInBanner);
                return elements.Count > 0 ? elements[0] : null;
            });
        return banner!.Text;
    }

    // "Q-007" out of either check-in banner, so a caller can carry the number on to the
    // queue views without re-reading it from the API.
    public static string QueueNumberFrom(string bannerText)
    {
        var match = Regex.Match(bannerText, @"Q-\d+");
        return match.Success
            ? match.Value
            : throw new InvalidOperationException($"No queue number found in check-in banner text: '{bannerText}'");
    }

    private static By CheckInButton => By.XPath("//button[normalize-space()='Check In']");

    // Matches on normalize-space(.), not text(): the JSX interpolates the patient's name and
    // the queue number as separate expressions ({patient.fullName} checked in. Queue:
    // {queueStatus.queueNumber}), which React renders as three separate DOM text nodes.
    // XPath's text() returns a node-set, and contains(text(), ...) only tests the *first*
    // node in it - here, the patient's name - so it never sees "checked in." at all.
    // normalize-space(.) instead takes the element's whole string value, concatenated across
    // every child the way Selenium's own .Text does.
    private static By CheckInSuccessBanner =>
        By.XPath("//p[contains(normalize-space(.), 'checked in.')][contains(normalize-space(.), 'Queue:')]");

    private static By AlreadyCheckedInBanner =>
        By.XPath("//p[contains(normalize-space(.), 'Already checked in')][contains(normalize-space(.), 'Queue:')]");

    private IReadOnlyList<string> ConditionColumnText(int columnIndex) =>
        _driver.FindElements(By.XPath(
                $"//p[normalize-space()='Chronic Conditions']/following::table[1]//tbody/tr/td[{columnIndex}]"))
            .Select(e => e.Text.Trim())
            .ToList();

    private static By ChronicConditionAlertBanner => By.XPath(
        "//div[@role='alert'][.//p[normalize-space()='Chronic Condition Alert']]");

    // Matches the clinic-local calendar date PatientService and the frontend now validate
    // against (Asia/Colombo, fixed since dad9e3b / commit series SWC-81), not the machine's
    // own local time or UTC. Kept as a member here because the condition tests read it
    // through this page object; the calculation itself lives in Support/ClinicClock.cs,
    // which Support/QueueDatabase.cs also needs.
    public static string ClinicTodayIsoDate() => ClinicClock.TodayIsoDate();

    private IReadOnlyList<string> ColumnText(string cellSelector) =>
        _driver.FindElements(By.CssSelector($"table tbody tr {cellSelector}"))
            .Select(e => e.Text.Trim())
            .Where(t => t.Length > 0)
            .ToList();

    private IWebElement RowActionButton(string allergyName, string buttonText) =>
        _driver.FindElement(By.XPath(
            $"//tr[td[normalize-space()='{allergyName}']]//button[normalize-space()='{buttonText}']"));

    private string DemographicValue(string label) =>
        _driver.FindElement(By.XPath(
            $"//dt[normalize-space()='{label}']/following-sibling::dd[1]")).Text.Trim();

    private static By AlertBanner => By.CssSelector("div[role='alert']");

    private static By RowCell(string name) => By.XPath($"//table//td[normalize-space()='{name}']");
}
