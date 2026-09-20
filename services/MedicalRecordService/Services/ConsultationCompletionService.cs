using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;

namespace MedicalRecordService.Services;

public sealed class ConsultationCompletionService : IConsultationCompletionService
{
    private readonly IConsultationCompletionRepository _repository;
    private readonly IConsultationCompletedPublisher _publisher;
    private readonly ILogger<ConsultationCompletionService> _logger;

    public ConsultationCompletionService(
        IConsultationCompletionRepository repository,
        IConsultationCompletedPublisher publisher,
        ILogger<ConsultationCompletionService> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<ConsultationProgressResponse?> FindByQueueAsync(
        Guid queueId,
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        if (queueId == Guid.Empty || doctorId == Guid.Empty)
        {
            throw new ArgumentException("Queue and doctor IDs must be provided.");
        }

        return _repository.FindByQueueAsync(queueId, doctorId, cancellationToken);
    }

    public async Task<CompleteConsultationResult> CompleteAsync(
        Guid consultationId,
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        if (consultationId == Guid.Empty || doctorId == Guid.Empty)
        {
            throw new ArgumentException("Consultation and doctor IDs must be provided.");
        }

        var preparation = await _repository.PrepareAsync(
            consultationId, doctorId, cancellationToken);

        if (preparation.Outcome == CompletionPreparationOutcome.ConsultationNotFound)
        {
            return new CompleteConsultationResult(CompleteConsultationOutcome.ConsultationNotFound);
        }

        if (preparation.Outcome == CompletionPreparationOutcome.VitalSignsMissing)
        {
            return new CompleteConsultationResult(CompleteConsultationOutcome.VitalSignsMissing);
        }

        var completedEvent = preparation.Event
            ?? throw new InvalidOperationException("A ready consultation has no completion event.");

        try
        {
            if (await _publisher.PublishAsync(completedEvent, cancellationToken))
            {
                return new CompleteConsultationResult(
                    CompleteConsultationOutcome.Success, completedEvent.EventId);
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The database commit has already happened. Keep the stored ID for retry.
            _logger.LogError(
                "Consultation completion publish failed: consultationId={ConsultationId} eventId={EventId}",
                consultationId, completedEvent.EventId);
        }

        return new CompleteConsultationResult(CompleteConsultationOutcome.PublishFailed);
    }
}
