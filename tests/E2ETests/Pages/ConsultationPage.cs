using E2ETests.Support;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

public class ConsultationPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public ConsultationPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
    }

    private IWebElement TemplateSelect => _driver.FindElement(By.Id("templateId"));
    private IWebElement SymptomsInput => _driver.FindElement(By.Id("symptoms"));
    private IWebElement ExaminationFindingsInput => _driver.FindElement(By.Id("examinationFindings"));
    private IWebElement DiagnosisInput => _driver.FindElement(By.Id("diagnosis"));
    private IWebElement NotesInput => _driver.FindElement(By.Id("notes"));
    private IWebElement SaveButton => _driver.FindElement(By.CssSelector("button[type='submit']"));

    public void WaitUntilLoaded()
    {
        _wait.Until(d => d.FindElements(By.Id("symptoms")).Count > 0);
    }

    public string CurrentConsultationContext => _driver.FindElement(By.XPath(
        "//section[.//p[normalize-space()='Current Consultation']]")).Text;

    // The templates dropdown is populated from GET /api/templates after mount, so a
    // caller must wait for the real options (not just the "Loading templates..."
    // placeholder) before selecting one.
    public void WaitUntilTemplatesLoaded()
    {
        _wait.Until(_ => new SelectElement(TemplateSelect).Options.Count > 1);
    }

    public void SelectTemplate(string templateName)
    {
        WaitUntilTemplatesLoaded();
        new SelectElement(TemplateSelect).SelectByText(templateName);
    }

    // Chrome normalizes newlines to \r\n when a textarea's .value is read back,
    // regardless of how the value was set - not something the app controls, so it is
    // normalized away here rather than baked into every caller's expected string.
    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");

    public string SymptomsValue => NormalizeNewlines(SymptomsInput.GetDomProperty("value") ?? string.Empty);
    public string ExaminationFindingsValue =>
        NormalizeNewlines(ExaminationFindingsInput.GetDomProperty("value") ?? string.Empty);
    public string NotesValue => NormalizeNewlines(NotesInput.GetDomProperty("value") ?? string.Empty);

    // Appends rather than replaces, so a caller can prove the pre-filled template text
    // is still there (AC2 - "doctor can edit the pre-filled text freely"), not that the
    // field was cleared and retyped.
    public void AppendToSymptoms(string text) => SymptomsInput.SendKeys(text);

    public void AppendToExaminationFindings(string text) => ExaminationFindingsInput.SendKeys(text);

    public void AppendToNotes(string text) => NotesInput.SendKeys(text);

    public void FillSymptoms(string text)
    {
        SymptomsInput.Clear();
        SymptomsInput.SendKeys(text);
    }

    public void FillExaminationFindings(string text)
    {
        ExaminationFindingsInput.Clear();
        ExaminationFindingsInput.SendKeys(text);
    }

    public void FillDiagnosis(string text)
    {
        DiagnosisInput.Clear();
        DiagnosisInput.SendKeys(text);
    }

    public void FillNotes(string text)
    {
        NotesInput.Clear();
        NotesInput.SendKeys(text);
    }

    public void ClickSave() => SaveButton.Click();

    // The heading's className includes "uppercase", and Chrome's element.Text returns
    // the CSS-rendered (transformed) text rather than the DOM text content - the same
    // issue AppSession.ClickNavLink documents for link text - so this matches
    // case-insensitively via XPath translate() rather than depending on casing.
    private static readonly By SavedConfirmationLocator = By.XPath(
        "//p[translate(normalize-space(), " +
        "'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')='consultation saved']");

    public void WaitForSavedConfirmation()
    {
        _wait.Until(d => d.FindElements(SavedConfirmationLocator).Count > 0);
    }

    public bool HasSavedConfirmation() => _driver.FindElements(SavedConfirmationLocator).Count > 0;

    // SWC-25 vital signs are a second form on this page, shown only after the
    // consultation is saved. Scope its submit button so it cannot select the
    // consultation form's Save Consultation button above it.
    private IWebElement VitalSignsSection => _driver.FindElement(By.XPath(
        "//section[.//h2[normalize-space()='Record Vital Signs']]"));

    private IWebElement VitalInput(string fieldId) => VitalSignsSection.FindElement(By.Id(fieldId));

    public void WaitForVitalSignsForm() =>
        _wait.Until(d => d.FindElements(By.Id("heightCentimeters")).Count > 0);

    public void EnterVital(string fieldId, string value) => VitalInput(fieldId).SendKeys(value);

    // React-controlled number inputs do not reliably update their component state
    // after Selenium Clear(). Use the native setter and dispatch input/change.
    public void ClearVital(string fieldId) => Browser.SetInputValue(_driver, VitalInput(fieldId), "");

    public string VitalValue(string fieldId) => VitalInput(fieldId).GetDomProperty("value") ?? string.Empty;

    public bool IsVitalInputEnabled(string fieldId) => VitalInput(fieldId).Enabled;

    public string BmiText => VitalSignsSection.FindElement(By.Id("bmi")).Text.Trim();

    public bool BmiIsOutputOnly =>
        VitalSignsSection.FindElement(By.Id("bmi")).TagName == "output" &&
        VitalSignsSection.FindElements(By.CssSelector("input#bmi, textarea#bmi")).Count == 0;

    public string? VitalWarning(string fieldId) => TryGetText(By.Id($"{fieldId}-warning"));

    private IWebElement SaveVitalsButton => VitalSignsSection.FindElement(By.CssSelector("button[type='submit']"));

    public bool IsSaveVitalsEnabled => SaveVitalsButton.Enabled;

    public void ClickSaveVitals() => SaveVitalsButton.Click();

    public string WaitForVitalsSavedMessage() => _wait.Until(_ =>
        VitalSignsSection.FindElements(By.XPath(
            ".//p[contains(normalize-space(), 'Measurements were linked to this consultation')]"))
            .FirstOrDefault()?.Text)
        ?? throw new InvalidOperationException("The vital signs save confirmation was not shown.");

    public string? SymptomsFieldError => TryGetText(By.Id("symptoms-error"));

    public string? DiagnosisFieldError => TryGetText(By.Id("diagnosis-error"));

    // SWC-26/SWC-38's Complete Consultation section, rendered under vital signs once the
    // consultation is created. Scoped by its own heading so the button and messages here
    // cannot collide with the Save Consultation or Save Vitals submit buttons above it.
    private IWebElement CompleteConsultationSection => _driver.FindElement(By.XPath(
        "//section[.//h2[normalize-space()='Complete Consultation']]"));

    private IWebElement CompleteConsultationButton => CompleteConsultationSection.FindElement(
        By.XPath(".//button[normalize-space()='Complete Consultation' or normalize-space()='Completing...']"));

    public bool IsCompleteConsultationEnabled => CompleteConsultationButton.Enabled;

    public void WaitUntilCompleteConsultationIsEnabled() => _wait.Until(_ => CompleteConsultationButton.Enabled);

    public void ClickCompleteConsultation() => CompleteConsultationButton.Click();

    public string? CompleteConsultationBlockedMessage => TryGetText(
        CompleteConsultationSection, By.XPath(".//p[normalize-space()='Please save vital signs first']"));

    private string? TryGetText(By locator) => TryGetText(_driver, locator);

    private static string? TryGetText(ISearchContext context, By locator)
    {
        var elements = context.FindElements(locator);
        return elements.Count > 0 ? elements[0].Text : null;
    }
}
