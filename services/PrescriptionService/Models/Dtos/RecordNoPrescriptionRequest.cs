using System.ComponentModel.DataAnnotations;

namespace PrescriptionService.Models.Dtos;

// The consultation comes from the route and the doctor from the Gateway identity headers.
public sealed class RecordNoPrescriptionRequest : IValidatableObject
{
    // Nullable so that an ID left out of the body is reported as missing, instead of
    // quietly becoming the all-zero GUID.
    public Guid? QueueId { get; init; }
    public Guid? PatientId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (QueueId is null || QueueId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Queue ID is required",
                [nameof(QueueId)]);
        }

        if (PatientId is null || PatientId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Patient ID is required",
                [nameof(PatientId)]);
        }
    }
}
