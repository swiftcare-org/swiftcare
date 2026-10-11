using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Admin;

[Trait("Category", "E2E")]
[Trait("Story", "SWC-135")]
public sealed class Sprint4AdminWorkflowsTests : SeleniumTestBase
{
    [Fact]
    public void Admin_CreatesAndDeactivatesStaffAccount()
    {
        AppSession.LogIn(Driver, "admin.fernando");
        AppSession.GoTo(Driver, UserManagementPage.Path);

        var users = new UserManagementPage(Driver);
        users.WaitUntilLoaded();
        var username = TestData.Username("sprint4.staff");
        users.FillForm(username, TestData.Password(), TestData.FullName("Sprint 4 Staff"), "Receptionist", null);
        users.Submit();

        Assert.Contains("created successfully", users.WaitForSuccessMessage());
        Assert.True(users.UserRowExists(username));
        Assert.Equal("Active", users.UserRowStatus(username), ignoreCase: true);

        users.OpenDeactivatePanel(username);
        users.ConfirmDeactivate();
        users.WaitForStatus(username, "Inactive");

        Assert.Contains("Account Deactivated", users.WaitForActionNotice());
        Assert.Equal("Inactive", users.UserRowStatus(username), ignoreCase: true);
    }

    [Fact]
    public void Admin_AuditLogShowsOwnAccountManagementAction()
    {
        AppSession.LogIn(Driver, "admin.fernando");
        AppSession.GoTo(Driver, UserManagementPage.Path);

        var users = new UserManagementPage(Driver);
        users.WaitUntilLoaded();
        var username = TestData.Username("sprint4.audit");
        users.FillForm(username, TestData.Password(), TestData.FullName("Sprint 4 Audit"), "Receptionist", null);
        users.Submit();
        Assert.Contains("created successfully", users.WaitForSuccessMessage());

        AppSession.GoTo(Driver, AuditLogPage.Path);
        var audit = new AuditLogPage(Driver);
        audit.WaitUntilLoaded();
        audit.WaitForEntry("admin.fernando", "Created account", username);
    }

    [Fact]
    public void Admin_DailyAndMonthlyReportsRenderBothServiceSections()
    {
        AppSession.LogIn(Driver, "admin.fernando");

        AppSession.GoTo(Driver, DailyReportPage.Path);
        var daily = new DailyReportPage(Driver);
        daily.WaitUntilLoaded();
        Assert.True(daily.HasActivityData);
        Assert.True(daily.HasPrescriptionData);
        Assert.False(daily.ActivityUnavailable);
        Assert.False(daily.PrescriptionsUnavailable);

        AppSession.GoTo(Driver, MonthlyReportPage.Path);
        var monthly = new MonthlyReportPage(Driver);
        monthly.WaitUntilLoaded();
        Assert.True(monthly.HasActivityData);
        Assert.True(monthly.HasPrescriptionData);
        Assert.False(monthly.ActivityUnavailable);
        Assert.False(monthly.PrescriptionsUnavailable);
    }
}
