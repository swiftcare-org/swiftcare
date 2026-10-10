using System.ComponentModel.DataAnnotations;
using PatientService.Services;

namespace PatientService.Models.Validation;

// A patient's date of birth must be a real, already-lived date: not in the future, and not
// old enough to be a data-entry typo (e.g. a transposed year). 130 years is a generous upper
// bound rather than a clinical claim about maximum human lifespan.
public sealed class PastDateAttribute : ValidationAttribute
{
    private const int MaximumAgeYears = 130;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not DateOnly dateOfBirth)
        {
            return ValidationResult.Success;
        }

        var provider = validationContext.GetService(typeof(IClinicDateProvider)) as IClinicDateProvider
            ?? throw new InvalidOperationException("IClinicDateProvider is required for birth-date validation.");
        var today = provider.Today;
        return dateOfBirth <= today && dateOfBirth >= today.AddYears(-MaximumAgeYears)
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage, [validationContext.MemberName!]);
    }
}
