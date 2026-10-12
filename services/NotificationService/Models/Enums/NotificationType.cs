namespace NotificationService.Models.Enums;

// One value per Kafka topic the service listens to.
public enum NotificationType
{
    PatientCheckedIn,
    PatientCalled,
    ConsultationCompleted
}
