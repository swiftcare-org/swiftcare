using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The admin "User Management" screen (SWC-8). Route and selectors mirror
// frontend/src/pages/UserManagementPage.tsx.
public class UserManagementPage
{
    public const string Path = "/admin/users";

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public UserManagementPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
    }

    private IWebElement UsernameInput => _driver.FindElement(By.Id("username"));
    private IWebElement PasswordInput => _driver.FindElement(By.Id("password"));
    private IWebElement FullNameInput => _driver.FindElement(By.Id("fullName"));
    private SelectElement RoleSelect => new(_driver.FindElement(By.Id("role")));
    private IWebElement SubmitButton => _driver.FindElement(By.CssSelector("button[type='submit']"));

    public void WaitUntilLoaded() => _wait.Until(d => d.FindElements(By.Id("username")).Count > 0);

    // The room-number field is rendered only while the selected role is Doctor.
    public bool HasRoomNumberField => _driver.FindElements(By.Id("roomNumber")).Count > 0;

    public void SelectRole(string role) => RoleSelect.SelectByValue(role);

    public void FillForm(string username, string password, string fullName, string role, string? roomNumber)
    {
        UsernameInput.Clear();
        UsernameInput.SendKeys(username);
        PasswordInput.Clear();
        PasswordInput.SendKeys(password);
        FullNameInput.Clear();
        FullNameInput.SendKeys(fullName);
        RoleSelect.SelectByValue(role);

        if (roomNumber is not null)
        {
            var room = _driver.FindElement(By.Id("roomNumber"));
            room.Clear();
            room.SendKeys(roomNumber);
        }
    }

    public void Submit() => SubmitButton.Click();

    // The success body text ("...created successfully.") is not upper-cased by CSS,
    // unlike the "Account Created" heading, so it is the reliable thing to match.
    public string WaitForSuccessMessage()
    {
        var region = _wait.Until(d => d.FindElement(By.CssSelector("[aria-live='polite']")));
        _wait.Until(_ => region.Text.Contains("created successfully"));
        return region.Text;
    }

    public bool HasSuccessBanner =>
        _driver.FindElements(By.CssSelector("[aria-live='polite']")).Any(e => e.Text.Contains("created successfully"));

    // Field id is the input's id; the error <p> is "<id>-error" (e.g. "username-error").
    public string? GetFieldError(string fieldId)
    {
        var elements = _driver.FindElements(By.Id($"{fieldId}-error"));
        return elements.Count > 0 ? elements[0].Text : null;
    }

    public bool UserRowExists(string username) =>
        _wait.Until(d => d.FindElements(RowCell(username)).Count > 0);

    public string UserRowStatus(string username) =>
        _driver.FindElement(By.XPath(
            $"//tr[td[normalize-space()='{username}']]/td[last()]")).Text;

    private static By RowCell(string username) =>
        By.XPath($"//table//td[normalize-space()='{username}']");
}
