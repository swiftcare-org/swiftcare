namespace QueueService.Models.Enums;

public enum CallNextPatientOutcome
{
    Success,
    NoPatientsWaiting,
    DoctorOrRoomOccupied,
    EventPublishFailed,

    // Other doctors kept calling patients at the same moment and every attempt was rolled
    // back by the database. Nothing changed; the doctor can simply try again.
    ConcurrentCallConflict
}
