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
}
