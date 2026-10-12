using QueueService.Models.Enums;

namespace QueueService.Models.Dtos;

public sealed class CallNextPatientResult
{
    public required CallNextPatientOutcome Outcome { get; init; }
    public CalledPatientResponse? CalledPatient { get; init; }
}
