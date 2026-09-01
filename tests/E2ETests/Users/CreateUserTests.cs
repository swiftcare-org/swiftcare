using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Users;

// Covers SWC-8 (Create User Account) against the real frontend, Gateway and
// AuthService. Happy path plus the two UI-observable negatives; the server-side
// rules (duplicate username, weak password) are covered by
// AuthService.UnitTests and the API suite and are not re-tested through the
// browser. See tests/E2ETests/README.md for run prerequisites.
[Trait("Category", "E2E")]
public class CreateUserTests : SeleniumTestBase
{
    // Scenario 1 - successful creation: password is hashed server-side and the
    // account shows up in the list.
    [Fact]
    public void CreateDoctor_WithValidDetails_ShowsSuccessAndListsTheAccount()
    {
        AppSession.LogIn(Driver, "admin.fernando");
        AppSession.GoTo(Driver, UserManagementPage.Path);

        var page = new UserManagementPage(Driver);
        page.WaitUntilLoaded();

        var username = TestData.Username("doctor");
        page.FillForm(username, TestData.Password(), TestData.FullName("Doctor"), "Doctor", TestData.RoomNumber());
        page.Submit();

        Assert.Contains("created successfully", page.WaitForSuccessMessage());
        Assert.True(page.UserRowExists(username));
        Assert.Equal("Active", page.UserRowStatus(username));
    }

    // Scenario 4 - missing required fields: client-side validation blocks the
    // request entirely.
    [Fact]
    public void Submit_WithEmptyFields_ShowsValidationErrorsAndCreatesNothing()
    {
        AppSession.LogIn(Driver, "admin.fernando");
        AppSession.GoTo(Driver, UserManagementPage.Path);

        var page = new UserManagementPage(Driver);
        page.WaitUntilLoaded();
        page.Submit();

        Assert.Equal("Username is required.", page.GetFieldError("username"));
        Assert.Equal("Password is required.", page.GetFieldError("password"));
        Assert.Equal("Full name is required.", page.GetFieldError("fullName"));
        // Role defaults to Doctor, so Scenario 5's room-number rule fires here too.
        Assert.Equal("Room number is required for doctors", page.GetFieldError("roomNumber"));
        Assert.Contains(UserManagementPage.Path, Driver.Url);
        Assert.False(page.HasSuccessBanner);
    }

    // Scenario 5 (UI side) - the room-number field only exists for the Doctor
    // role; picking another role removes it, reselecting Doctor brings it back.
    [Fact]
    public void RoleSelection_TogglesRoomNumberField()
    {
        AppSession.LogIn(Driver, "admin.fernando");
        AppSession.GoTo(Driver, UserManagementPage.Path);

        var page = new UserManagementPage(Driver);
        page.WaitUntilLoaded();

        Assert.True(page.HasRoomNumberField, "Room number should be visible for the default Doctor role.");

        page.SelectRole("Receptionist");
        Assert.False(page.HasRoomNumberField, "Room number should be hidden for non-Doctor roles.");

        page.SelectRole("Doctor");
        Assert.True(page.HasRoomNumberField, "Room number should reappear when Doctor is reselected.");
    }
}
