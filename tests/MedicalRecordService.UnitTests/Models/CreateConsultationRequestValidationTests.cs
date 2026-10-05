using System.ComponentModel.DataAnnotations;
using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.UnitTests.Models;

public class CreateConsultationRequestValidationTests
{
    [Fact]
    public void NewRequestFailsTheRequiredClinicalFields()
    {
        var messages = Validate(new CreateConsultationRequest()).Select(result => result.ErrorMessage);

        Assert.Contains("Symptoms are required", messages);
        Assert.Contains("Diagnosis is required", messages);
    }

    [Fact]
    public void FollowUpDateWithoutInstructionsIsRejected()
    {
        var request = ValidRequest(followUpDate: new DateOnly(2026, 11, 1), instructions: null);

        var result = Assert.Single(Validate(request));

        Assert.Equal("Follow-up instructions are required when a follow-up date is provided", result.ErrorMessage);
        Assert.Equal([nameof(CreateConsultationRequest.FollowUpInstructions)], result.MemberNames);
    }

    [Theory]
    [InlineData("Review blood pressure")]
    [InlineData("   ")]
    public void FollowUpInstructionsWithoutDateAreRejectedUnlessBlank(string instructions)
    {
        var request = ValidRequest(followUpDate: null, instructions: instructions);

        var results = Validate(request);

        if (string.IsNullOrWhiteSpace(instructions))
        {
            Assert.Empty(results);
            return;
        }

        var result = Assert.Single(results);
        Assert.Equal("Follow-up date is required when follow-up instructions are provided", result.ErrorMessage);
        Assert.Equal([nameof(CreateConsultationRequest.FollowUpDate)], result.MemberNames);
    }

    [Fact]
    public void FollowUpDateWithInstructionsIsAccepted()
    {
        var request = ValidRequest(followUpDate: new DateOnly(2026, 11, 1), instructions: "Review blood pressure");

        Assert.Empty(Validate(request));
    }

    private static CreateConsultationRequest ValidRequest(DateOnly? followUpDate, string? instructions) => new()
    {
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        Symptoms = "Headache",
        Diagnosis = "Tension headache",
        FollowUpDate = followUpDate,
        FollowUpInstructions = instructions
    };

    private static List<ValidationResult> Validate(CreateConsultationRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
