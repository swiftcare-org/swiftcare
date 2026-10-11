using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueueService.Controllers;
using QueueService.Data;
using QueueService.Models.Entities;
using QueueService.Models.Enums;

namespace QueueService.UnitTests.Controllers;

public sealed class InternalVisitsTests
{
    [Theory]
    [InlineData(QueueStatus.InConsultation, "IN_CONSULTATION")]
    [InlineData(QueueStatus.Waiting, "INELIGIBLE")]
    [InlineData(QueueStatus.Completed, "INELIGIBLE")]
    public async Task LookupPreservesTheAuthoritativeVisitAndState(QueueStatus status, string expected)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new QueueDbContext(new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var entry = new QueueEntry
        {
            PatientId = Guid.NewGuid(),
            QueueDate = new DateOnly(2026, 9, 1),
            QueueNumber = "Q-001",
            Status = status,
            DoctorId = status == QueueStatus.Waiting ? null : Guid.NewGuid()
        };
        context.QueueEntries.Add(entry);
        await context.SaveChangesAsync();
        var controller = new InternalVisitsController(context);
        Assert.IsType<NotFoundResult>(await controller.GetAsync(Guid.NewGuid(), CancellationToken.None));
        var result = Assert.IsType<OkObjectResult>(await controller.GetAsync(entry.Id, CancellationToken.None));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.Equal(entry.Id, document.RootElement.GetProperty("QueueId").GetGuid());
        Assert.Equal(entry.PatientId, document.RootElement.GetProperty("PatientId").GetGuid());
        Assert.Equal(expected, document.RootElement.GetProperty("Status").GetString());
        if (entry.DoctorId.HasValue) Assert.Equal(entry.DoctorId.Value, document.RootElement.GetProperty("DoctorId").GetGuid());
        else Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("DoctorId").ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-secret")]
    public async Task LookupRequiresTheGatewaySecret(string? secret)
    {
        using var factory = new QueueServiceWebApplicationFactory();
        using var client = factory.CreateClient();
        if (secret is not null) client.DefaultRequestHeaders.Add("X-Gateway-Secret", secret);
        using var response = await client.GetAsync($"/internal/visits/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
