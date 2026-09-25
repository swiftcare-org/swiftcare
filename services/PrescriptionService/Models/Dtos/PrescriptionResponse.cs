namespace PrescriptionService.Models.Dtos;

public sealed record PrescriptionResponse(
    Guid Id,
    Guid ConsultationId,
    Guid QueueId,
    Guid PatientId,
    Guid DoctorId,
    string DoctorName,
    string Status,
    DateTime CreatedAt,
    IReadOnlyList<PrescriptionItemResponse> Medicines,
    string? DispensedBy = null,
    DateTime? DispensedAt = null);

public sealed record PrescriptionItemResponse(
    Guid Id,
    int ItemOrder,
    string MedicineName,
    string Dosage,
    string Frequency,
    string Duration,
    string? Instructions);
