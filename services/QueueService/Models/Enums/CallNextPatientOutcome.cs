namespace QueueService.Models.Enums;

public enum CallNextPatientOutcome
{
    Success,
    NoPatientsWaiting,
    DoctorOrRoomOccupied,
    EventPublishFailed
}
