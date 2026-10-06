namespace AuthService.Models.Dtos;

// What other staff may know about a doctor: no username, status or account dates.
public sealed class DoctorSummaryResponse
{
    public required Guid UserId { get; init; }
    public required string FullName { get; init; }
    public string? RoomNumber { get; init; }
    public string? Specialization { get; init; }
}
