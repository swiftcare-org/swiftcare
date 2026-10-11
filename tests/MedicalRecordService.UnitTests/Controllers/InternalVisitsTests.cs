using System.Net;
using MedicalRecordService.Controllers;
using MedicalRecordService.Data;
using MedicalRecordService.Models.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

public sealed class InternalVisitsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LookupReturnsOnlyTheRequestedVisit(bool found)
    {
        var id = Guid.NewGuid();
        var visit = new VisitContextResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "COMPLETE");
        using var cancellation = new CancellationTokenSource();
        var repository = new Mock<IVisitContextRepository>(MockBehavior.Strict);
        repository.Setup(item => item.FindAsync(id, cancellation.Token)).ReturnsAsync(found ? visit : null);
        var result = await new InternalVisitsController(repository.Object).GetAsync(id, cancellation.Token);
        if (found) Assert.Same(visit, Assert.IsType<OkObjectResult>(result).Value);
        else Assert.IsType<NotFoundResult>(result);
        repository.Verify(item => item.FindAsync(id, cancellation.Token), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-secret")]
    public async Task LookupWithoutGatewaySecretCannotReachRepository(string? secret)
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var repository = new Mock<IVisitContextRepository>(MockBehavior.Strict);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IVisitContextRepository>();
            services.AddScoped(_ => repository.Object);
        }));
        using var client = host.CreateClient();
        if (secret is not null) client.DefaultRequestHeaders.Add("X-Gateway-Secret", secret);
        using var response = await client.GetAsync($"/internal/visits/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TrustedLookupReturnsVisitThroughTheActualPipeline()
    {
        var id = Guid.NewGuid();
        var visit = new VisitContextResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "COMPLETE");
        var repository = new Mock<IVisitContextRepository>();
        repository.Setup(item => item.FindAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(visit);
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IVisitContextRepository>();
            services.AddScoped(_ => repository.Object);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Gateway-Secret", MedicalRecordServiceWebApplicationFactory.ValidGatewaySecret);
        using var response = await client.GetAsync($"/internal/visits/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(visit.PatientId.ToString(), body);
        Assert.Contains(visit.QueueId.ToString(), body);
        Assert.Contains(visit.DoctorId.ToString(), body);
    }
}
