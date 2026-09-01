using E2ETests.Config;
using E2ETests.Pages;

namespace E2ETests.Auth;

// Covers SWC-6 (User Login) acceptance criteria against the real frontend,
// AuthService and Gateway - not mocks. Requires docker-compose up (mysql,
// authservice, apigateway) and `npm run dev` in frontend/ to be running
// first; see tests/E2ETests/README.md.
//
// Tagged Category=E2E so this project can be run and filtered independently
// of the unit-test projects (e.g. `dotnet test --filter Category=E2E`).
[Trait("Category", "E2E")]
public class LoginTests : SeleniumTestBase
{
    // Scenario 1 - Successful login: each active seeded role lands on its
    // own dashboard.
    [Theory]
    [InlineData("dr.chen", "/doctor")]
    [InlineData("reception.silva", "/reception")]
    [InlineData("admin.fernando", "/admin")]
    public void Login_WithValidCredentials_RedirectsToRoleDashboard(string username, string expectedPathPrefix)
    {
        var loginPage = new LoginPage(Driver);
        loginPage.NavigateTo();

        loginPage.SubmitCredentials(username, TestConfig.SeedPassword);
        loginPage.WaitForRedirectAwayFromLogin();

        Assert.Contains(expectedPathPrefix, Driver.Url);
        Assert.NotNull(GetStoredToken());
    }

    // Scenario 2 - Invalid credentials.
    [Fact]
    public void Login_WithWrongPassword_ShowsErrorAndIssuesNoToken()
    {
        var loginPage = new LoginPage(Driver);
        loginPage.NavigateTo();

        loginPage.SubmitCredentials("dr.chen", "definitely-the-wrong-password");
        var message = loginPage.WaitForServerMessage();

        Assert.Contains("Invalid username or password", message);
        Assert.Contains("/login", Driver.Url);
        Assert.Null(GetStoredToken());
    }

    // Scenario 3 - Deactivated account (dr.rao, seeded inactive).
    [Fact]
    public void Login_WithDeactivatedAccount_ShowsDeactivatedMessageAndIssuesNoToken()
    {
        var loginPage = new LoginPage(Driver);
        loginPage.NavigateTo();

        loginPage.SubmitCredentials("dr.rao", TestConfig.SeedPassword);
        var message = loginPage.WaitForServerMessage();

        Assert.Contains("Your account has been deactivated", message);
        Assert.Null(GetStoredToken());
    }

    // Scenario 4 - Empty fields: client-side validation blocks submission,
    // no request is ever made.
    [Fact]
    public void Login_WithEmptyFields_ShowsValidationErrorsAndDoesNotAttemptLogin()
    {
        var loginPage = new LoginPage(Driver);
        loginPage.NavigateTo();

        loginPage.SubmitCredentials(string.Empty, string.Empty);

        Assert.Equal("Enter your username.", loginPage.GetUsernameFieldError());
        Assert.Equal("Enter your password.", loginPage.GetPasswordFieldError());
        Assert.Contains("/login", Driver.Url);
        Assert.Null(GetStoredToken());
    }
}
