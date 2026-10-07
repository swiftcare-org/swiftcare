using System.ComponentModel.DataAnnotations;

namespace PrescriptionService.Models.Dtos;

// The consultation comes from the route and the doctor from the Gateway identity headers.
public sealed class RecordNoPrescriptionRequest : IValidatableObject
{
    public Guid QueueId { get; init; }
    public Guid PatientId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (QueueId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Queue ID is required",
                [nameof(QueueId)]);
        }

        if (PatientId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Patient ID is required",
                [nameof(PatientId)]);
        }
    }
}
