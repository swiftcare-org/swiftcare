using E2ETests.Config;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

public class LoginPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public LoginPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
    }

    private IWebElement UsernameInput => _driver.FindElement(By.Id("username"));
    private IWebElement PasswordInput => _driver.FindElement(By.Id("password"));
    private IWebElement SubmitButton => _driver.FindElement(By.CssSelector("button[type='submit']"));

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl($"{TestConfig.BaseUrl}/login");
        _wait.Until(d => d.FindElements(By.Id("username")).Count > 0);
    }

    public void SubmitCredentials(string username, string password)
    {
        EnterControlledValue(By.Id("username"), username);
        EnterControlledValue(By.Id("password"), password);
        SubmitButton.Click();
    }

    // Field-level validation errors, e.g. "Enter your username." - rendered
    // synchronously, no request in flight.
    public string? GetUsernameFieldError() => TryGetText(By.Id("username-error"));

    public string? GetPasswordFieldError() => TryGetText(By.Id("password-error"));

    // Server-driven status message (invalid credentials / deactivated account),
    // rendered inside the page's single aria-live region after the login
    // request resolves.
    public string WaitForServerMessage()
    {
        var region = _wait.Until(d => d.FindElement(By.CssSelector("[aria-live='polite']")));
        _wait.Until(_ => region.Text.Length > 0);
        return region.Text;
    }

    public void WaitForRedirectAwayFromLogin()
    {
        new WebDriverWait(_driver, TimeSpan.FromSeconds(10))
            .Until(d => !d.Url.Contains("/login"));
    }

    // React owns both login inputs. Under concurrent Chrome load, sending the next
    // field immediately can race a controlled-input render and drop part of the
    // previous value. Verify the DOM value before submitting so the browser always
    // sends the credentials that the test supplied.
    private void EnterControlledValue(By locator, string value)
    {
        var input = _driver.FindElement(locator);
        input.Clear();
        input.SendKeys(value);
        new WebDriverWait(_driver, TimeSpan.FromSeconds(5))
            .Until(d => d.FindElement(locator).GetDomProperty("value") == value);
    }

    private string? TryGetText(By locator)
    {
        var elements = _driver.FindElements(locator);
        return elements.Count > 0 ? elements[0].Text : null;
    }
}
