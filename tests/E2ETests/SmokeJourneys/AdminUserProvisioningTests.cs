using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.SmokeJourneys;

// Cross-story smoke journey spanning SWC-8 -> SWC-7 -> SWC-6: an admin creates
// an account, signs out, and that brand-new account can log in and lands on its
// own role dashboard. Verifies the create -> authenticate handoff across a real
// logout, which no single-story test covers.
[Trait("Category", "E2E")]
[Trait("Category", "Smoke")]
public class AdminUserProvisioningTests : SeleniumTestBase
{
    [Fact]
    public void AdminCreatedUser_CanLogInAndReachesRoleDashboard()
    {
        AppSession.LogIn(Driver, "admin.fernando");
        AppSession.ClickNavLink(Driver, UserManagementPage.Path);

        var users = new UserManagementPage(Driver);
        users.WaitUntilLoaded();

        var username = TestData.Username("recept");
        var password = TestData.Password();
        users.FillForm(username, password, TestData.FullName("Reception"), "Receptionist", roomNumber: null);
        users.Submit();
        Assert.Contains("created successfully", users.WaitForSuccessMessage());

        new DashboardPage(Driver).SignOut();

        var login = new LoginPage(Driver);
        login.NavigateTo();
        login.SubmitCredentials(username, password);
        login.WaitForRedirectAwayFromLogin();

        Assert.Contains("/reception", Driver.Url);
        Assert.NotNull(GetStoredToken());
    }
}
