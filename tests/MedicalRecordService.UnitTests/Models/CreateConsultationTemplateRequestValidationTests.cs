using System.ComponentModel.DataAnnotations;
using MedicalRecordService.Models.Dtos;

namespace MedicalRecordService.UnitTests.Models;

// SWC-146: a template needs a name and all three clinical fields, each within its limit.
public class CreateConsultationTemplateRequestValidationTests
{
    private const int NameLimit = 100;
    private const int TextLimit = 5000;

    [Fact]
    public void ValidRequestPasses()
    {
        Assert.Empty(Validate(Request()));
    }

    [Fact]
    public void NewRequestFailsEveryRequiredField()
    {
        var messages = Validate(new CreateConsultationTemplateRequest())
            .Select(result => result.ErrorMessage)
            .ToList();

        Assert.Equal(4, messages.Count);
        Assert.Contains("Template name is required", messages);
        Assert.Contains("Symptoms are required", messages);
        Assert.Contains("Examination findings are required", messages);
        Assert.Contains("Notes are required", messages);
    }

    [Theory]
    [InlineData(nameof(CreateConsultationTemplateRequest.Name), "Template name is required")]
    [InlineData(nameof(CreateConsultationTemplateRequest.Symptoms), "Symptoms are required")]
    [InlineData(nameof(CreateConsultationTemplateRequest.ExaminationFindings), "Examination findings are required")]
    [InlineData(nameof(CreateConsultationTemplateRequest.Notes), "Notes are required")]
    public void WhitespaceOnlyFieldIsRejectedForThatFieldAlone(string field, string expectedMessage)
    {
        var result = Assert.Single(Validate(Request(field, "   ")));

        Assert.Equal(expectedMessage, result.ErrorMessage);
        Assert.Equal([field], result.MemberNames);
    }

    [Theory]
    [InlineData(nameof(CreateConsultationTemplateRequest.Name), NameLimit)]
    [InlineData(nameof(CreateConsultationTemplateRequest.Symptoms), TextLimit)]
    [InlineData(nameof(CreateConsultationTemplateRequest.ExaminationFindings), TextLimit)]
    [InlineData(nameof(CreateConsultationTemplateRequest.Notes), TextLimit)]
    public void FieldAtItsLimitPasses(string field, int limit)
    {
        Assert.Empty(Validate(Request(field, new string('a', limit))));
    }

    [Theory]
    [InlineData(
        nameof(CreateConsultationTemplateRequest.Name), NameLimit, "Template name must be 100 characters or fewer")]
    [InlineData(
        nameof(CreateConsultationTemplateRequest.Symptoms), TextLimit, "Symptoms must be 5000 characters or fewer")]
    [InlineData(
        nameof(CreateConsultationTemplateRequest.ExaminationFindings),
        TextLimit,
        "Examination findings must be 5000 characters or fewer")]
    [InlineData(
        nameof(CreateConsultationTemplateRequest.Notes), TextLimit, "Notes must be 5000 characters or fewer")]
    public void FieldOneCharacterOverItsLimitIsRejectedForThatFieldAlone(string field, int limit, string expectedMessage)
    {
        var result = Assert.Single(Validate(Request(field, new string('a', limit + 1))));

        Assert.Equal(expectedMessage, result.ErrorMessage);
        Assert.Equal([field], result.MemberNames);
    }

    [Fact]
    public void LimitsMatchTheDocumentedValues()
    {
        Assert.Equal(NameLimit, CreateConsultationTemplateRequest.NameMaxLength);
        Assert.Equal(TextLimit, CreateConsultationTemplateRequest.ClinicalTextMaxLength);
    }

    private static CreateConsultationTemplateRequest Request(string? field = null, string? value = null) => new()
    {
        Name = field == nameof(CreateConsultationTemplateRequest.Name) ? value! : "BP Review",
        Symptoms = field == nameof(CreateConsultationTemplateRequest.Symptoms) ? value! : "Headache",
        ExaminationFindings = field == nameof(CreateConsultationTemplateRequest.ExaminationFindings)
            ? value!
            : "Blood pressure",
        Notes = field == nameof(CreateConsultationTemplateRequest.Notes) ? value! : "Plan"
    };

    private static List<ValidationResult> Validate(CreateConsultationTemplateRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
