using System.ComponentModel.DataAnnotations;
using PrescriptionService.Models.Dtos;

namespace PrescriptionService.UnitTests.Models;

public class CreatePrescriptionRequestValidationTests
{
    [Fact]
    public void EmptyMedicineListIsRejectedWithRequiredMessage()
    {
        var request = new CreatePrescriptionRequest
        {
            ConsultationId = Guid.NewGuid(),
            QueueId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            Medicines = []
        };

        var errors = Validate(request);

        var error = Assert.Single(errors);
        Assert.Equal("Add at least one medicine", error.ErrorMessage);
        Assert.Contains(nameof(CreatePrescriptionRequest.Medicines), error.MemberNames);
    }

    [Fact]
    public void EmptyLinkIdentifiersAreRejected()
    {
        var request = new CreatePrescriptionRequest
        {
            Medicines = [ValidMedicine()]
        };

        var errors = Validate(request);

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreatePrescriptionRequest.ConsultationId)));
        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreatePrescriptionRequest.QueueId)));
        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreatePrescriptionRequest.PatientId)));
    }

    [Theory]
    [InlineData(nameof(PrescriptionItemRequest.MedicineName), "Medicine name is required")]
    [InlineData(nameof(PrescriptionItemRequest.Dosage), "Dosage is required")]
    [InlineData(nameof(PrescriptionItemRequest.Frequency), "Frequency is required")]
    [InlineData(nameof(PrescriptionItemRequest.Duration), "Duration is required")]
    public void RequiredMedicineFieldsRejectWhitespace(string propertyName, string expectedMessage)
    {
        var medicine = ValidMedicine();
        typeof(PrescriptionItemRequest).GetProperty(propertyName)!.SetValue(medicine, "   ");

        var errors = Validate(medicine);

        Assert.Contains(errors, error => error.ErrorMessage == expectedMessage);
    }

    private static PrescriptionItemRequest ValidMedicine() => new()
    {
        MedicineName = "Amoxicillin",
        Dosage = "500 mg",
        Frequency = "Twice daily",
        Duration = "5 days"
    };

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, true);
        return results;
    }
}
