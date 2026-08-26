using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using PatientService.Models.Dtos;
using PatientService.Models.Enums;

namespace PatientService.UnitTests.Models;

public class RegisterPatientRequestValidationTests
{
    // Mirrors the JsonStringEnumConverter registered in Program.cs - JsonSerializer.Serialize
    // with default options ignores JsonStringEnumMemberName entirely and would emit the enum
    // as an integer instead of "A+".
    private static readonly JsonSerializerOptions StringEnumOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static RegisterPatientRequest ValidRequest() => new()
    {
        Nic = "200012345678",
        FullName = "Synthetic Test Patient",
        DateOfBirth = new DateOnly(1990, 4, 17),
        Gender = Gender.Female,
        Address = "123 Synthetic Lane, Colombo",
        PhoneNumber = "0771234567",
        BloodGroup = BloodGroup.APositive
    };

    private static IList<ValidationResult> Validate(RegisterPatientRequest request)
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ValidRequestProducesNoValidationErrors()
    {
        var errors = Validate(ValidRequest());
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("200012345678")] // 12-digit current format
    [InlineData("991234567V")]   // 9 digits + V, old format
    [InlineData("991234567X")]   // 9 digits + X, old format
    [InlineData("991234567v")]   // lowercase v accepted at the DTO level
    public void ValidNicFormatsPassValidation(string nic)
    {
        var request = ValidRequest();
        request.Nic = nic;

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456789")]      // 9 digits, no V/X suffix
    [InlineData("1234567890")]     // 10 digits, not a valid length
    [InlineData("99123456V")]      // only 8 digits before the suffix
    [InlineData("2000123456789")]  // 13 digits
    [InlineData("991234567VX")]    // two suffix characters
    public void InvalidNicFormatsFailValidation(string nic)
    {
        var request = ValidRequest();
        request.Nic = nic;

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterPatientRequest.Nic)));
    }

    [Theory]
    [InlineData("0771234567")]     // local mobile
    [InlineData("0112345678")]     // local landline
    [InlineData("+94771234567")]   // international mobile
    [InlineData("+94112345678")]   // international landline
    public void ValidPhoneNumberFormatsPassValidation(string phoneNumber)
    {
        var request = ValidRequest();
        request.PhoneNumber = phoneNumber;

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("07712345678")]    // 11 digits after the leading 0
    [InlineData("+1234567890")]    // foreign country code
    [InlineData("94771234567")]    // missing the leading + on the international form
    public void InvalidPhoneNumberFormatsFailValidation(string phoneNumber)
    {
        var request = ValidRequest();
        request.PhoneNumber = phoneNumber;

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterPatientRequest.PhoneNumber)));
    }

    [Theory]
    [InlineData("A+")]
    [InlineData("A-")]
    [InlineData("B+")]
    [InlineData("B-")]
    [InlineData("O+")]
    [InlineData("O-")]
    [InlineData("AB+")]
    [InlineData("AB-")]
    public void AllEightBloodGroupsAreValid(string bloodGroupWireValue)
    {
        // Exercises the actual JSON wire format (e.g. "A+") through deserialization, not the
        // C# enum member name - this is the direction that matters, since the controller
        // parses an incoming request body the same way. Deserialize, not Serialize: the
        // default JSON encoder unicode-escapes the plus sign on output (a harmless,
        // JSON-valid encoding), which would make a Serialize-based lookup brittle here.
        var parsed = JsonSerializer.Deserialize<BloodGroup>($"\"{bloodGroupWireValue}\"", StringEnumOptions);

        var request = ValidRequest();
        request.BloodGroup = parsed;

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void MissingBloodGroupFailsValidation()
    {
        var request = ValidRequest();
        request.BloodGroup = null;

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterPatientRequest.BloodGroup)));
    }

    [Fact]
    public void FutureDateOfBirthFailsValidation()
    {
        var request = ValidRequest();
        request.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterPatientRequest.DateOfBirth)));
    }

    [Fact]
    public void ImplausiblyOldDateOfBirthFailsValidation()
    {
        var request = ValidRequest();
        request.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-131);

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterPatientRequest.DateOfBirth)));
    }

    [Fact]
    public void TodayAsDateOfBirthPassesValidation()
    {
        var request = ValidRequest();
        request.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow);

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(nameof(RegisterPatientRequest.Nic), "")]
    [InlineData(nameof(RegisterPatientRequest.FullName), "")]
    [InlineData(nameof(RegisterPatientRequest.Address), "")]
    [InlineData(nameof(RegisterPatientRequest.PhoneNumber), "")]
    public void MissingRequiredStringFieldFailsValidationOnThatField(string fieldName, string emptyValue)
    {
        var request = ValidRequest();
        typeof(RegisterPatientRequest).GetProperty(fieldName)!.SetValue(request, emptyValue);

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(fieldName));
    }

    [Fact]
    public void MissingGenderFailsValidation()
    {
        var request = ValidRequest();
        request.Gender = null;

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterPatientRequest.Gender)));
    }

    [Fact]
    public void MissingDateOfBirthFailsValidation()
    {
        var request = ValidRequest();
        request.DateOfBirth = null;

        var errors = Validate(request);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterPatientRequest.DateOfBirth)));
    }
}
