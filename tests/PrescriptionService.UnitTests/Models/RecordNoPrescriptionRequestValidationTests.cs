using System.ComponentModel.DataAnnotations;
using PrescriptionService.Models.Dtos;

namespace PrescriptionService.UnitTests.Models;

// SWC-130: the decision must name the queue entry and the patient it belongs to.
public class RecordNoPrescriptionRequestValidationTests
{
    [Fact]
    public void RequestWithBothIdsPasses()
    {
        Assert.Empty(Validate(new RecordNoPrescriptionRequest
        {
            QueueId = Guid.NewGuid(),
            PatientId = Guid.NewGuid()
        }));
    }

    [Fact]
    public void NewRequestFailsBothIds()
    {
        var results = Validate(new RecordNoPrescriptionRequest());

        Assert.Equal(
            ["Queue ID is required", "Patient ID is required"],
            results.Select(result => result.ErrorMessage));
    }

    [Fact]
    public void MissingQueueIdIsRejectedForThatFieldAlone()
    {
        var result = Assert.Single(Validate(new RecordNoPrescriptionRequest { PatientId = Guid.NewGuid() }));

        Assert.Equal("Queue ID is required", result.ErrorMessage);
        Assert.Equal([nameof(RecordNoPrescriptionRequest.QueueId)], result.MemberNames);
    }

    [Fact]
    public void MissingPatientIdIsRejectedForThatFieldAlone()
    {
        var result = Assert.Single(Validate(new RecordNoPrescriptionRequest { QueueId = Guid.NewGuid() }));

        Assert.Equal("Patient ID is required", result.ErrorMessage);
        Assert.Equal([nameof(RecordNoPrescriptionRequest.PatientId)], result.MemberNames);
    }

    private static List<ValidationResult> Validate(RecordNoPrescriptionRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
