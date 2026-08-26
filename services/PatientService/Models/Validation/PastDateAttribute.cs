using System.ComponentModel.DataAnnotations;

namespace PatientService.Models.Validation;

// A patient's date of birth must be a real, already-lived date: not in the future, and not
// old enough to be a data-entry typo (e.g. a transposed year). 130 years is a generous upper
// bound rather than a clinical claim about maximum human lifespan.
public sealed class PastDateAttribute : ValidationAttribute
{
    private const int MaximumAgeYears = 130;

    public override bool IsValid(object? value)
    {
        if (value is not DateOnly dateOfBirth)
        {
            return true;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return dateOfBirth <= today && dateOfBirth >= today.AddYears(-MaximumAgeYears);
    }
}
