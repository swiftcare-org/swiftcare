using E2ETests.Support;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace E2ETests.Pages;

// The receptionist "Register Patient" screen (SWC-9). Route and selectors mirror
// frontend/src/pages/PatientRegistrationPage.tsx.
public class PatientRegistrationPage
{
    public const string Path = "/reception/patients/new";

    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;

    public PatientRegistrationPage(IWebDriver driver)
    {
        _driver = driver;
        _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
    }

    private IWebElement NicInput => _driver.FindElement(By.Id("nic"));
    private IWebElement FullNameInput => _driver.FindElement(By.Id("fullName"));
    private IWebElement DateOfBirthInput => _driver.FindElement(By.Id("dateOfBirth"));
    private IWebElement AddressInput => _driver.FindElement(By.Id("address"));
    private IWebElement PhoneInput => _driver.FindElement(By.Id("phoneNumber"));
    private IWebElement SubmitButton => _driver.FindElement(By.CssSelector("button[type='submit']"));

    public void WaitUntilLoaded() => _wait.Until(d => d.FindElements(By.Id("nic")).Count > 0);

    // Gender and blood group keep their defaults (Male / O+); those never trip
    // validation, so the tests leave them alone.
    public void FillForm(string nic, string fullName, string isoDateOfBirth, string address, string phone)
    {
        NicInput.Clear();
        NicInput.SendKeys(nic);
        FullNameInput.Clear();
        FullNameInput.SendKeys(fullName);
        Browser.SetInputValue(_driver, DateOfBirthInput, isoDateOfBirth);
        AddressInput.Clear();
        AddressInput.SendKeys(address);
        PhoneInput.Clear();
        PhoneInput.SendKeys(phone);
    }

    public void Submit() => SubmitButton.Click();

    public string WaitForSuccessMessage()
    {
        var region = _wait.Until(d => d.FindElement(By.CssSelector("[aria-live='polite']")));
        _wait.Until(_ => region.Text.Contains("registered successfully"));
        return region.Text;
    }

    public string? GetFieldError(string fieldId)
    {
        var elements = _driver.FindElements(By.Id($"{fieldId}-error"));
        return elements.Count > 0 ? elements[0].Text : null;
    }
}
