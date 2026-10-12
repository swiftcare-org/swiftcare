namespace PatientService.Services;

public interface IClinicDateProvider
{
    DateOnly Today { get; }
}
