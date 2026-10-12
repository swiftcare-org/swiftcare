using System.ComponentModel.DataAnnotations;
using PatientService.Services;

namespace PatientService.Models.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotFutureClinicDateAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(
        object? value,
        ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is not DateOnly date)
        {
            return new ValidationResult(ErrorMessage);
        }

        var clinicDateProvider = validationContext.GetService(typeof(IClinicDateProvider))
            as IClinicDateProvider
            ?? throw new InvalidOperationException(
                $"{nameof(IClinicDateProvider)} is required for clinic-date validation.");

        return date > clinicDateProvider.Today
            ? new ValidationResult(ErrorMessage, [validationContext.MemberName!])
            : ValidationResult.Success;
    }
}
