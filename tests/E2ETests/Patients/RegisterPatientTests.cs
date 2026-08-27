using E2ETests.Pages;
using E2ETests.Support;

namespace E2ETests.Patients;

// Covers SWC-9 (Register New Patient). Happy path plus the client-side
// validation gate; duplicate-NIC and invalid-blood-group are server rules
// covered by PatientService.UnitTests and the API suite. The AC's queue-number
// text and "search for existing patient" link depend on SWC-19 (not yet built)
// and are asserted there, not here. See tests/E2ETests/README.md.
[Trait("Category", "E2E")]
public class RegisterPatientTests : SeleniumTestBase
{
    // Scenario 1 - successful registration: today the UI confirms the record was
    // created (queue-number assertions belong to SWC-19).
    [Fact]
    public void RegisterPatient_WithValidDetails_ShowsSuccessMessage()
    {
        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, PatientRegistrationPage.Path);

        var page = new PatientRegistrationPage(Driver);
        page.WaitUntilLoaded();
        page.FillForm(
            TestData.Nic(),
            TestData.FullName("Patient"),
            "1990-05-15",
            "1 Test Lane, Colombo",
            TestData.Phone());
        page.Submit();

        Assert.Contains("registered successfully", page.WaitForSuccessMessage());
    }

    // Scenario 3 - missing required fields: per-field errors, no request made.
    [Fact]
    public void Submit_WithEmptyFields_ShowsValidationErrorsAndRegistersNothing()
    {
        AppSession.LogIn(Driver, "reception.silva");
        AppSession.GoTo(Driver, PatientRegistrationPage.Path);

        var page = new PatientRegistrationPage(Driver);
        page.WaitUntilLoaded();
        page.Submit();

        Assert.Equal("NIC is required.", page.GetFieldError("nic"));
        Assert.Equal("Full name is required.", page.GetFieldError("fullName"));
        Assert.Equal("Date of birth is required.", page.GetFieldError("dateOfBirth"));
        Assert.Equal("Address is required.", page.GetFieldError("address"));
        Assert.Equal("Phone number is required.", page.GetFieldError("phoneNumber"));
        Assert.Contains(PatientRegistrationPage.Path, Driver.Url);
    }
}
