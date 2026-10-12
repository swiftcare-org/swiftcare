using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;
using Moq;

namespace MedicalRecordService.UnitTests.Services;

// SWC-146: listing, saving and removing consultation templates.
public class ConsultationTemplateServiceTests
{
    private static readonly Guid DoctorId = Guid.NewGuid();

    private readonly Mock<IConsultationTemplateRepository> _repository = new(MockBehavior.Strict);

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 4, 30, 0, TimeSpan.Zero);

    private ConsultationTemplateService CreateService() => new(_repository.Object, new FixedTimeProvider());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ConsultationTemplate Template(string name, Guid? ownerId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Symptoms = $"{name} symptoms",
        ExaminationFindings = $"{name} findings",
        Notes = $"{name} notes",
        CreatedByDoctorId = ownerId
    };

    [Fact]
    public async Task ListReturnsWhatTheRepositoryHoldsForThatDoctorInTheSameOrder()
    {
        var builtIn = Template("General Consultation");
        var own = Template("BP Review", DoctorId);
        _repository
            .Setup(repository => repository.ListVisibleToDoctorAsync(DoctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([builtIn, own]);

        var templates = await CreateService().GetTemplatesForDoctorAsync(DoctorId);

        Assert.Equal(new[] { builtIn.Id, own.Id }, templates.Select(template => template.Id).ToArray());
        Assert.Equal("BP Review", templates[1].Name);
        Assert.Equal("BP Review symptoms", templates[1].Symptoms);
        Assert.Equal("BP Review findings", templates[1].ExaminationFindings);
        Assert.Equal("BP Review notes", templates[1].Notes);
    }

    [Fact]
    public async Task ListMarksATemplateWithNoOwnerAsBuiltInAndAnOwnedOneAsNot()
    {
        _repository
            .Setup(repository => repository.ListVisibleToDoctorAsync(DoctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Template("General Consultation"), Template("BP Review", DoctorId)]);

        var templates = await CreateService().GetTemplatesForDoctorAsync(DoctorId);

        Assert.True(templates[0].IsBuiltIn);
        Assert.False(templates[1].IsBuiltIn);
    }

    // Privacy rests on the query being scoped to the caller: the service never asks the
    // repository for anyone else's templates.
    [Fact]
    public async Task ListAsksTheRepositoryOnlyForTheRequestingDoctor()
    {
        _repository
            .Setup(repository => repository.ListVisibleToDoctorAsync(DoctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateService().GetTemplatesForDoctorAsync(DoctorId);

        _repository.Verify(
            repository => repository.ListVisibleToDoctorAsync(DoctorId, It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListWithoutADoctorIdIsRejectedBeforeTheRepositoryIsCalled()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().GetTemplatesForDoctorAsync(Guid.Empty));

        Assert.Equal("doctorId", exception.ParamName);
        Assert.StartsWith("Doctor ID must be provided.", exception.Message);
        _repository.VerifyNoOtherCalls();
    }

    private static CreateConsultationTemplateRequest SaveRequest(string name = "BP Review") => new()
    {
        Name = name,
        Symptoms = "Headache:\n- ",
        ExaminationFindings = "Blood pressure:\n- ",
        Notes = "Plan:\n- "
    };

    [Fact]
    public async Task CreateSavesTheTemplateAgainstTheRequestingDoctor()
    {
        ConsultationTemplate? saved = null;
        _repository
            .Setup(repository => repository.TryAddAsync(It.IsAny<ConsultationTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<ConsultationTemplate, CancellationToken>((template, _) => saved = template)
            .ReturnsAsync(true);

        var result = await CreateService().CreateAsync(SaveRequest(), DoctorId);

        Assert.Equal(CreateTemplateOutcome.Created, result.Outcome);
        Assert.NotNull(saved);
        Assert.NotEqual(Guid.Empty, saved!.Id);
        Assert.Equal(DoctorId, saved.CreatedByDoctorId);
        Assert.True(saved.IsActive);
        Assert.Equal(Now.UtcDateTime, saved.CreatedAt);
        Assert.Equal("BP Review", saved.Name);
        Assert.Equal("Headache:\n- ", saved.Symptoms);
        Assert.Equal("Blood pressure:\n- ", saved.ExaminationFindings);
        Assert.Equal("Plan:\n- ", saved.Notes);
    }

    [Fact]
    public async Task CreateReturnsTheSavedTemplateAsTheDoctorsOwn()
    {
        ConsultationTemplate? saved = null;
        _repository
            .Setup(repository => repository.TryAddAsync(It.IsAny<ConsultationTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<ConsultationTemplate, CancellationToken>((template, _) => saved = template)
            .ReturnsAsync(true);

        var result = await CreateService().CreateAsync(SaveRequest(), DoctorId);

        Assert.NotNull(result.Template);
        Assert.Equal(saved!.Id, result.Template!.Id);
        Assert.Equal("BP Review", result.Template.Name);
        Assert.Equal("Headache:\n- ", result.Template.Symptoms);
        Assert.Equal("Blood pressure:\n- ", result.Template.ExaminationFindings);
        Assert.Equal("Plan:\n- ", result.Template.Notes);
        Assert.False(result.Template.IsBuiltIn);
    }

    [Fact]
    public async Task CreateTrimsTheNameButKeepsTheClinicalTextAsTyped()
    {
        ConsultationTemplate? saved = null;
        _repository
            .Setup(repository => repository.TryAddAsync(It.IsAny<ConsultationTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<ConsultationTemplate, CancellationToken>((template, _) => saved = template)
            .ReturnsAsync(true);

        await CreateService().CreateAsync(SaveRequest(name: "  BP Review  "), DoctorId);

        Assert.Equal("BP Review", saved!.Name);
        Assert.EndsWith("- ", saved.Symptoms);
    }

    [Fact]
    public async Task CreateWithANameTheDoctorAlreadyUsesReturnsDuplicateNameAndNoTemplate()
    {
        _repository
            .Setup(repository => repository.TryAddAsync(It.IsAny<ConsultationTemplate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateService().CreateAsync(SaveRequest(), DoctorId);

        Assert.Equal(CreateTemplateOutcome.DuplicateName, result.Outcome);
        Assert.Null(result.Template);
    }

    [Fact]
    public async Task CreateWithoutADoctorIdIsRejectedBeforeAnythingIsSaved()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(SaveRequest(), Guid.Empty));

        Assert.Equal("doctorId", exception.ParamName);
        _repository.VerifyNoOtherCalls();
    }

    private void SetupFind(ConsultationTemplate? template) =>
        _repository
            .Setup(repository => repository.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

    [Fact]
    public async Task RemoveDeactivatesTheDoctorsOwnTemplate()
    {
        var own = Template("BP Review", DoctorId);
        SetupFind(own);
        _repository
            .Setup(repository => repository.DeactivateAsync(own.Id, DoctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var outcome = await CreateService().RemoveAsync(own.Id, DoctorId);

        Assert.Equal(RemoveTemplateOutcome.Removed, outcome);
        _repository.Verify(repository => repository.FindAsync(own.Id, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(
            repository => repository.DeactivateAsync(own.Id, DoctorId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveBuiltInTemplateIsRefusedAndNothingIsDeactivated()
    {
        var builtIn = Template("General Consultation");
        SetupFind(builtIn);

        var outcome = await CreateService().RemoveAsync(builtIn.Id, DoctorId);

        Assert.Equal(RemoveTemplateOutcome.BuiltIn, outcome);
        _repository.Verify(
            repository => repository.DeactivateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemoveAnotherDoctorsTemplateIsReportedAsMissingAndNothingIsDeactivated()
    {
        var someoneElses = Template("BP Review", Guid.NewGuid());
        SetupFind(someoneElses);

        var outcome = await CreateService().RemoveAsync(someoneElses.Id, DoctorId);

        Assert.Equal(RemoveTemplateOutcome.NotFound, outcome);
        _repository.Verify(
            repository => repository.DeactivateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemoveUnknownTemplateReturnsNotFound()
    {
        SetupFind(null);

        var outcome = await CreateService().RemoveAsync(Guid.NewGuid(), DoctorId);

        Assert.Equal(RemoveTemplateOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task RemoveAnAlreadyRemovedTemplateReturnsNotFoundAndNothingIsDeactivated()
    {
        var removed = Template("BP Review", DoctorId);
        removed.IsActive = false;
        SetupFind(removed);

        var outcome = await CreateService().RemoveAsync(removed.Id, DoctorId);

        Assert.Equal(RemoveTemplateOutcome.NotFound, outcome);
        _repository.Verify(
            repository => repository.DeactivateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Two requests can pass the checks together; only the one that changed a row succeeded.
    [Fact]
    public async Task RemoveThatChangesNoRowReturnsNotFound()
    {
        var own = Template("BP Review", DoctorId);
        SetupFind(own);
        _repository
            .Setup(repository => repository.DeactivateAsync(own.Id, DoctorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var outcome = await CreateService().RemoveAsync(own.Id, DoctorId);

        Assert.Equal(RemoveTemplateOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task RemoveWithoutADoctorIdIsRejectedBeforeTheRepositoryIsCalled()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().RemoveAsync(Guid.NewGuid(), Guid.Empty));

        Assert.Equal("doctorId", exception.ParamName);
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveWithoutATemplateIdIsRejectedBeforeTheRepositoryIsCalled()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().RemoveAsync(Guid.Empty, DoctorId));

        Assert.Equal("templateId", exception.ParamName);
        Assert.StartsWith("Template ID must be provided.", exception.Message);
        _repository.VerifyNoOtherCalls();
    }
}
