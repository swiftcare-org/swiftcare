namespace PatientService.Services;

public interface IPatientEventPublisher
{
    // Returns false rather than throwing on failure: a lost patient-checked-in event must
    // never fail the registration request itself (see PatientRegistrationService), so the
    // caller decides what "publish failed" means rather than having to catch an exception.
    Task<bool> PublishPatientCheckedInAsync(
        Guid patientId,
        bool isNewPatient,
        string correlationId,
        CancellationToken cancellationToken = default);
}
