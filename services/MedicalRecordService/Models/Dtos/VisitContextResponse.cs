namespace MedicalRecordService.Models.Dtos;

public sealed record VisitContextResponse(Guid PatientId, Guid QueueId, Guid DoctorId, string Status);
