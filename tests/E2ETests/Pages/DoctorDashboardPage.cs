using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The doctor's dashboard, which is one screen carrying two stories: the shared waiting
// pool (SWC-21) and the call-next action with its current-patient panel (SWC-22). One page
// object covers both, per the QA-01 page-object rule, so the consultation tests that only
// use call-next as arrangement and the SWC-21/SWC-22 tests that assert on it directly share
// the same selectors.
//
// Every waiting-pool accessor is scoped by queue number rather than by row index: the pool
// is shared across the whole clinic day on a local stack, so rows from other tests and
// earlier runs sit in the same table.
public class DoctorDashboardPage
{
    public const string Path = "/doctor";

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public DoctorDashboardPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
    }

    private IWebElement CallNextButton =>
        _driver.FindElement(By.XPath("//button[contains(normalize-space(), 'Call Next Patient')]"));

    public void WaitUntilLoaded()
    {
        _wait.Until(d => d.FindElements(By.Id("waiting-pool-heading")).Count > 0);
    }

    // --- Shared waiting pool (SWC-21) ---

    // Reads the DOM text content rather than Selenium's rendered .Text: the headings are
    // upper-cased by CSS (text-transform), which .Text reflects and textContent does not -
    // the same issue documented on severity text and nav-link text elsewhere in this suite.
    public IReadOnlyList<string> ColumnHeadings =>
        _driver.FindElements(By.CssSelector("table thead th"))
            .Select(element => (element.GetDomProperty("textContent") ?? string.Empty).Trim())
            .ToList();

    public IReadOnlyList<string> WaitingQueueNumbersInOrder =>
        _driver.FindElements(By.CssSelector("table tbody tr td:first-child"))
            .Select(element => element.Text.Trim())
            .Where(text => text.Length > 0)
            .ToList();

    public bool ShowsEmptyPoolMessage =>
        _driver.FindElements(By.XPath("//*[normalize-space()='No patients currently waiting']")).Count > 0;

    // The queue number a patient is waiting under, or null while their row is not on
    // screen. Resolved from one DOM query over the whole body rather than a number-then-name
    // pair of lookups, because the pool re-renders every five seconds and a two-step read can
    // land between renders and throw on a row that has just been replaced.
    public string? QueueNumberForPatient(string fullName)
    {
        foreach (var row in _driver.FindElements(By.CssSelector("table tbody tr")))
        {
            try
            {
                var cells = row.FindElements(By.TagName("td"));
                if (cells.Count >= 2 && cells[1].Text.Trim() == fullName)
                {
                    return cells[0].Text.Trim();
                }
            }
            catch (StaleElementReferenceException)
            {
                // The poll replaced this row mid-read; the caller retries.
                return null;
            }
        }

        return null;
    }

    public string WaitForPatientRow(string fullName) =>
        _wait.Until(_ => QueueNumberForPatient(fullName))
        ?? throw new InvalidOperationException($"No waiting-pool row rendered for '{fullName}'.");

    public string PatientNameFor(string queueNumber) => CellText(queueNumber, 2);

    public string CheckInTimeFor(string queueNumber) => CellText(queueNumber, 3);

    // Neither of these navigates, on purpose: passing them proves the dashboard's own
    // five-second poll (DoctorDashboard.tsx, POLL_INTERVAL_MS) picked up a change made
    // elsewhere, which is what makes them a real check of AC2's auto-refresh rather than a
    // restatement of the API-level Postman assertions.
    public void WaitForWaitingRow(string queueNumber) =>
        _wait.Until(d => d.FindElements(RowFor(queueNumber)).Count > 0);

    public void WaitForWaitingRowGone(string queueNumber, TimeSpan timeout) =>
        new WebDriverWait(_driver, timeout).Until(d => d.FindElements(RowFor(queueNumber)).Count == 0);

    // --- Call next and the current-patient panel (SWC-22) ---

    // The Call Next Patient button stays disabled until the 5-second waiting-pool poll
    // has loaded at least one entry - see DoctorDashboard.tsx, callNextDisabled.
    public bool IsCallNextEnabled => CallNextButton.Enabled;

    public void WaitUntilCallNextIsEnabled()
    {
        _wait.Until(_ => CallNextButton.Enabled);
    }

    public void WaitUntilCallNextIsDisabled()
    {
        _wait.Until(_ => !CallNextButton.Enabled);
    }

    public void ClickCallNextPatient()
    {
        WaitUntilCallNextIsEnabled();
        CallNextButton.Click();
    }

    // "Currently with you: Q-007 QA Synthetic Patient" - proves the call actually
    // resolved and the dashboard rendered the assigned patient, before navigating on.
    public string WaitForCurrentPatientPanel()
    {
        var panel = _wait.Until(d => d.FindElement(By.XPath("//p[contains(text(), 'Currently with you:')]")));
        return panel.Text;
    }

    public bool HasCurrentPatientPanel =>
        _driver.FindElements(By.XPath("//p[contains(text(), 'Currently with you:')]")).Count > 0;

    public void WaitUntilCurrentPatientStateLoaded() =>
        _wait.Until(d => d.FindElements(By.XPath(
            "//p[contains(normalize-space(), 'Loading your current consultation')]")).Count == 0);

    public bool HasCurrentPatientLoadError =>
        _driver.FindElements(By.XPath("//*[normalize-space()='Current Consultation Unavailable']")).Count > 0;

    // SWC-92 turns both identifiers in the current-patient line into links to the same
    // patient profile. Keep these selectors scoped to that line so they cannot match a
    // patient link rendered in the shared waiting pool below it.
    public string CurrentPatientQueueNumberLinkText => CurrentPatientLink(1).Text.Trim();

    public string CurrentPatientNameLinkText => CurrentPatientLink(2).Text.Trim();

    public string CurrentPatientQueueNumberProfilePath => ProfilePath(CurrentPatientLink(1));

    public string CurrentPatientNameProfilePath => ProfilePath(CurrentPatientLink(2));

    public int CurrentPatientProfileLinkCount =>
        _driver.FindElements(CurrentPatientLinks).Count;

    public void ClickCurrentPatientQueueNumber() => CurrentPatientLink(1).Click();

    public void ClickCurrentPatientName() => CurrentPatientLink(2).Click();

    // "Room R-204", rendered under the current-patient line from the room on the calling
    // doctor's own account rather than from anything the browser sent.
    public string CurrentRoomText =>
        _driver.FindElement(By.XPath("//p[starts-with(normalize-space(), 'Room ')]")).Text.Trim();

    // The queue number out of the current-patient panel, so a caller can follow it into the
    // waiting pool, the full queue or the public display.
    public static string QueueNumberFrom(string panelText)
    {
        var match = Regex.Match(panelText, @"Q-\d+");
        return match.Success
            ? match.Value
            : throw new InvalidOperationException($"No queue number found in panel text: '{panelText}'");
    }

    // Matches on href rather than link text: the link's className includes "uppercase",
    // and Selenium's By.LinkText matches the rendered (CSS-transformed) text, not the
    // DOM text content - see AppSession.ClickNavLink's identical note.
    public void ClickRecordConsultation()
    {
        var link = _wait.Until(d => d.FindElement(By.CssSelector("a[href='/doctor/consultation']")));
        link.Click();
    }

    private string CellText(string queueNumber, int columnIndex) =>
        _driver.FindElement(By.XPath(
            $"//table//tr[td[1][normalize-space()='{queueNumber}']]/td[{columnIndex}]")).Text.Trim();

    private IWebElement CurrentPatientLink(int position) =>
        _wait.Until(d => d.FindElement(By.XPath(
            $"//p[contains(normalize-space(), 'Currently with you:')]/a[{position}]")));

    private static string ProfilePath(IWebElement link)
    {
        var href = link.GetDomAttribute("href")
            ?? throw new InvalidOperationException("Current-patient profile link has no href.");
        return Uri.TryCreate(href, UriKind.Absolute, out var absolute)
            ? absolute.AbsolutePath
            : href;
    }

    private static readonly By CurrentPatientLinks =
        By.XPath("//p[contains(normalize-space(), 'Currently with you:')]/a");

    private static By RowFor(string queueNumber) =>
        By.XPath($"//table//tr[td[1][normalize-space()='{queueNumber}']]");
}
