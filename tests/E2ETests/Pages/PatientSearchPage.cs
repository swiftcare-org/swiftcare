using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The "Patient Search" screen (SWC-12), reachable by Doctor, Receptionist and
// Admin. Route and selectors mirror frontend/src/pages/PatientSearchPage.tsx.
public class PatientSearchPage
{
    public const string Path = "/patients/search";

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public PatientSearchPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
    }

    private IWebElement SearchInput => _driver.FindElement(By.Id("patientSearch"));

    public void WaitUntilLoaded() => _wait.Until(d => d.FindElements(By.Id("patientSearch")).Count > 0);

    // Results are debounced ~300ms after the last keystroke.
    public void Search(string term)
    {
        SearchInput.Clear();
        SearchInput.SendKeys(term);
    }

    public void WaitForResultRow(string fullName) => _wait.Until(d => d.FindElements(ResultLink(fullName)).Count > 0);

    public void OpenResult(string fullName) => _driver.FindElement(ResultLink(fullName)).Click();

    public string WaitForEmptyState()
    {
        var region = _wait.Until(d => d.FindElement(By.CssSelector("[aria-live='polite']")));
        _wait.Until(_ => region.Text.Contains("No patients found"));
        return region.Text;
    }

    // The "register" prompt is shown only to receptionists on the empty state.
    public bool HasRegisterLink => _driver.FindElements(RegisterLink).Count > 0;

    public string RegisterLinkHref => _driver.FindElement(RegisterLink).GetDomProperty("href");

    private static By ResultLink(string fullName) =>
        By.XPath($"//table//td//a[normalize-space()='{fullName}']");

    private static By RegisterLink =>
        By.XPath("//a[normalize-space()='Register a new patient']");
}
