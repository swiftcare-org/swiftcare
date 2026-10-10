namespace ApiGateway.Security;

public interface IClinicalVisitValidator
{
    Task<bool> ValidateAsync(Guid doctorId, Guid patientId, Guid queueId, Guid? consultationId,
        CancellationToken cancellationToken);
}
