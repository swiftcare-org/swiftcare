using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The receptionist's "Full Patient Queue" screen (SWC-20). Route and selectors mirror
// frontend/src/pages/QueueManagementPage.tsx.
//
// Every row accessor is scoped to the row carrying a given queue number rather than to a
// row index: this page shows the whole of today's queue on a shared local stack, so the
// entries another test or an earlier run left behind sit in the same table, and an
// index-based selector would read whichever row happened to be first.
public class QueueManagementPage
{
    public const string Path = "/reception/queue";

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public QueueManagementPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
    }

    public void WaitUntilLoaded() =>
        _wait.Until(d => d.FindElements(By.XPath("//h2[normalize-space()='Full Patient Queue']")).Count > 0);

    // AC1's column contract, read off the rendered header row.
    // Reads the DOM text content rather than Selenium's rendered .Text: the headings are
    // upper-cased by CSS (text-transform), which .Text reflects and textContent does not -
    // the same issue documented on severity text and nav-link text elsewhere in this suite.
    public IReadOnlyList<string> ColumnHeadings =>
        _driver.FindElements(By.CssSelector("table thead th"))
            .Select(element => (element.GetDomProperty("textContent") ?? string.Empty).Trim())
            .ToList();

    public bool ShowsEmptyQueueMessage =>
        _driver.FindElements(By.XPath("//*[normalize-space()=\"No patients in today's queue yet\"]")).Count > 0;

    public IReadOnlyList<string> QueueNumbersInOrder =>
        _driver.FindElements(By.CssSelector("table tbody tr td:first-child"))
            .Select(element => element.Text.Trim())
            .Where(text => text.Length > 0)
            .ToList();

    // Waits for a queue number to show up without ever navigating, so a caller can prove
    // AC3's five-second self-refresh picked up a change made elsewhere rather than proving
    // only that a reload shows it.
    public void WaitForQueueRow(string queueNumber) =>
        _wait.Until(d => d.FindElements(RowFor(queueNumber)).Count > 0);

    // SWC-38 - QueueService's completion consumer runs asynchronously off the Kafka
    // event, so a caller waits on the page's own five-second poll to show the new status
    // rather than asserting on it immediately after the API call that triggers it.
    public void WaitForStatus(string queueNumber, string statusText, TimeSpan timeout) =>
        new WebDriverWait(_driver, timeout).Until(_ => StatusFor(queueNumber).Contains(statusText));

    // The queue number allocated to a patient, or null while the row is not on screen.
    // Resolved from one DOM query over the whole body rather than a number-then-name pair
    // of lookups, because the table re-renders every five seconds and a two-step read can
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

    // Blocks until the patient's row is rendered and returns its queue number. Never
    // navigates, so a pass proves the page's own five-second poll brought the row in.
    public string WaitForPatientRow(string fullName) =>
        _wait.Until(_ => QueueNumberForPatient(fullName))
        ?? throw new InvalidOperationException($"No queue row rendered for '{fullName}'.");

    public string PatientNameFor(string queueNumber) => CellText(queueNumber, 2);

    public string CheckInTimeFor(string queueNumber) => CellText(queueNumber, 3);

    // Rendered upper-case with a leading emoji, e.g. "\u23f3 WAITING" - callers assert with
    // Contains on the status word rather than on the exact decorated string.
    public string StatusFor(string queueNumber) => CellText(queueNumber, 4);

    public string RoomFor(string queueNumber) => CellText(queueNumber, 5);

    public string DoctorFor(string queueNumber) => CellText(queueNumber, 6);

    public string PrescriptionFor(string queueNumber) => CellText(queueNumber, 7);

    // SWC-30 keeps View Prescription available for a completed queue entry even before
    // the doctor saves a prescription, so the details page can show its normal waiting state.
    public bool HasViewPrescriptionAction(string queueNumber) =>
        _driver.FindElements(ViewPrescriptionActionLocator(queueNumber)).Count > 0;

    // A link is always "enabled" to Selenium, so callers check where it leads instead.
    public string ViewPrescriptionPath(string queueNumber) =>
        PathOf(_driver.FindElement(ViewPrescriptionActionLocator(queueNumber)));

    public void ClickViewPrescription(string queueNumber) =>
        _wait.Until(d => d.FindElement(ViewPrescriptionActionLocator(queueNumber))).Click();

    private static By ViewPrescriptionActionLocator(string queueNumber) => By.XPath(
        $"//table//tr[td[1][normalize-space()='{queueNumber}']]/td[7]//*[self::a or self::button][normalize-space()='View Prescription']");

    // SWC-41 - a dispensed entry's prescription cell becomes a "✅ DISPENSED" link, picked up
    // by the page's own five-second poll.
    public void WaitForDispensedStatus(string queueNumber, TimeSpan timeout) =>
        PollingWait(timeout).Until(d => d.FindElements(By.XPath(
            $"//table//tr[td[1][normalize-space()='{queueNumber}']]/td[7]//a[contains(normalize-space(), 'DISPENSED')]")).Count > 0);

    // --- Pending Prescriptions list (SWC-30) ---

    private static readonly By PendingSection =
        By.CssSelector("section[aria-labelledby='pending-prescriptions-heading']");

    // Queue numbers in list order, read from each entry's "Q-007 · Patient name" line.
    public IReadOnlyList<string> PendingPrescriptionQueueNumbers =>
        _driver.FindElements(By.CssSelector(
                "section[aria-labelledby='pending-prescriptions-heading'] li > div > p:first-child"))
            .Select(element => element.Text.Split('·')[0].Trim())
            .ToList();

    public void WaitForPendingPrescription(string queueNumber, TimeSpan timeout) =>
        PollingWait(timeout).Until(_ => PendingPrescriptionQueueNumbers.Contains(queueNumber));

    public bool ShowsAllPrescriptionsDispensed =>
        _driver.FindElement(PendingSection)
            .FindElements(By.XPath(".//p[normalize-space()='All prescriptions dispensed today']")).Count > 0;

    public void WaitForAllPrescriptionsDispensed(TimeSpan timeout) =>
        PollingWait(timeout).Until(_ => ShowsAllPrescriptionsDispensed);

    // The queue re-renders every five seconds, so a read can land on a replaced element.
    private WebDriverWait PollingWait(TimeSpan timeout)
    {
        var wait = new WebDriverWait(_driver, timeout);
        wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException));
        return wait;
    }

    private static string PathOf(IWebElement link)
    {
        var href = link.GetDomAttribute("href")
            ?? throw new InvalidOperationException("View Prescription link has no href.");
        return Uri.TryCreate(href, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : href;
    }

    private string CellText(string queueNumber, int columnIndex) =>
        _driver.FindElement(By.XPath(
            $"//table//tr[td[1][normalize-space()='{queueNumber}']]/td[{columnIndex}]")).Text.Trim();

    private static By RowFor(string queueNumber) =>
        By.XPath($"//table//tr[td[1][normalize-space()='{queueNumber}']]");
}
