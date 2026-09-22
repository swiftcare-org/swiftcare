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

    // SWC-38's disabled View Prescription action, shown only once a row is COMPLETED.
    // Read as the control itself rather than through PrescriptionFor's cell text, so a
    // caller can prove the button is there and disabled, not only that some text renders.
    public bool HasViewPrescriptionButton(string queueNumber) =>
        _driver.FindElements(ViewPrescriptionButtonLocator(queueNumber)).Count > 0;

    public bool IsViewPrescriptionEnabled(string queueNumber) =>
        _driver.FindElement(ViewPrescriptionButtonLocator(queueNumber)).Enabled;

    private static By ViewPrescriptionButtonLocator(string queueNumber) => By.XPath(
        $"//table//tr[td[1][normalize-space()='{queueNumber}']]/td[7]//button[normalize-space()='View Prescription']");

    private string CellText(string queueNumber, int columnIndex) =>
        _driver.FindElement(By.XPath(
            $"//table//tr[td[1][normalize-space()='{queueNumber}']]/td[{columnIndex}]")).Text.Trim();

    private static By RowFor(string queueNumber) =>
        By.XPath($"//table//tr[td[1][normalize-space()='{queueNumber}']]");
}
