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

    public string? SymptomsFieldError => TryGetText(By.Id("symptoms-error"));

    public string? DiagnosisFieldError => TryGetText(By.Id("diagnosis-error"));

    private string? TryGetText(By locator)
    {
        var elements = _driver.FindElements(locator);
        return elements.Count > 0 ? elements[0].Text : null;
    }
}
