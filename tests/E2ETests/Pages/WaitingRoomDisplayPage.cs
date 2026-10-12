using E2ETests.Config;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// Page object for the public waiting-room display (SWC-23). Unlike every other page
// object in this suite, callers navigate straight here with no login step first - that
// is the whole point of the route.
public class WaitingRoomDisplayPage
{
    public const string Path = "/queue/display";

    private readonly IWebDriver _driver;

    public WaitingRoomDisplayPage(IWebDriver driver)
    {
        _driver = driver;
    }

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl($"{TestConfig.BaseUrl}{Path}");
        new WebDriverWait(_driver, TimeSpan.FromSeconds(10))
            .Until(d => d.FindElements(By.XPath("//h2[normalize-space()='Current Rooms']")).Count > 0);
    }

    // The queue number currently shown for a room, or null if that room is not on the
    // display right now. Re-queries the live DOM on every call and never navigates, so
    // a caller can use this inside WaitForRoomAssignment to prove the page updates
    // itself rather than requiring a fresh page load.
    public string? GetQueueNumberForRoom(string roomNumber) =>
        _driver
            .FindElements(By.XPath($"//p[normalize-space()='Room {roomNumber}']/following-sibling::p[1]"))
            .Select(element => element.Text)
            .FirstOrDefault();

    // Blocks until the given room shows the given queue number, or throws once timeout
    // elapses. Deliberately contains no Navigate call: the only way this can pass is if
    // the page's own five-second poll (WaitingRoomDisplayPage.tsx, POLL_INTERVAL_MS)
    // picked up a change made elsewhere, which is what makes this a genuine proof of
    // AC5's auto-refresh rather than a re-statement of the API-level Postman check.
    public void WaitForRoomAssignment(string roomNumber, string queueNumber, TimeSpan timeout) =>
        new WebDriverWait(_driver, timeout).Until(_ => GetQueueNumberForRoom(roomNumber) == queueNumber);

    public string BodyText => _driver.FindElement(By.TagName("body")).Text;
}
