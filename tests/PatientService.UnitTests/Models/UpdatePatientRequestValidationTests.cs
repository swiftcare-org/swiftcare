using System.ComponentModel.DataAnnotations;
using PatientService.Models.Dtos;
using PatientService.Models.Enums;

namespace PatientService.UnitTests.Models;

public class UpdatePatientRequestValidationTests
{
    private static UpdatePatientRequest ValidRequest() => new()
    {
        Address = "456 Updated Road, Colombo",
        PhoneNumber = "0777654321",
        BloodGroup = BloodGroup.ONegative
    };

    private static IList<ValidationResult> Validate(UpdatePatientRequest request)
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ValidRequestProducesNoValidationErrors()
    {
        Assert.Empty(Validate(ValidRequest()));
    }

    [Theory]
    [InlineData(nameof(UpdatePatientRequest.Address))]
    [InlineData(nameof(UpdatePatientRequest.PhoneNumber))]
    public void MissingRequiredStringFieldFailsValidation(string fieldName)
    {
        var request = ValidRequest();
        typeof(UpdatePatientRequest).GetProperty(fieldName)!.SetValue(request, string.Empty);

        var errors = Validate(request);

        Assert.Contains(errors, error => error.MemberNames.Contains(fieldName));
    }

    [Fact]
    public void MissingBloodGroupFailsValidation()
    {
        var request = ValidRequest();
        request.BloodGroup = null;

        var errors = Validate(request);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(UpdatePatientRequest.BloodGroup)));
    }

    [Theory]
    [InlineData("0771234567")]
    [InlineData("0112345678")]
    [InlineData("+94771234567")]
    public void SupportedPhoneNumberFormatsPassValidation(string phoneNumber)
    {
        var request = ValidRequest();
        request.PhoneNumber = phoneNumber;

        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("07712345678")]
    [InlineData("94771234567")]
    public void InvalidPhoneNumberFailsValidation(string phoneNumber)
    {
        var request = ValidRequest();
        request.PhoneNumber = phoneNumber;

        var errors = Validate(request);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(UpdatePatientRequest.PhoneNumber)));
    }
}
