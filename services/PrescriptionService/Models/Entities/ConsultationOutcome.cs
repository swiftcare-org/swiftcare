namespace PrescriptionService.Models.Entities;

// Both outcome types claim the same consultation key in the same transaction as their write.
public sealed class ConsultationOutcome
{
    public Guid ConsultationId { get; set; }
    public required string Outcome { get; set; }
}
