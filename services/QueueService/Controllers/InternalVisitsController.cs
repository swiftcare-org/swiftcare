using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QueueService.Data;
using QueueService.Models.Enums;

namespace QueueService.Controllers;

// Available only inside the gateway-secret trust boundary; no public proxy route exposes it.
[ApiController]
[Route("internal/visits")]
public sealed class InternalVisitsController(QueueDbContext context) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var visit = await context.QueueEntries.AsNoTracking().Where(entry => entry.Id == id)
            .Select(entry => new { entry.PatientId, QueueId = entry.Id, entry.DoctorId, entry.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (visit is null) return NotFound();
        return Ok(new
        {
            visit.PatientId,
            visit.QueueId,
            visit.DoctorId,
            Status = visit.Status == QueueStatus.InConsultation ? "IN_CONSULTATION" : "INELIGIBLE"
        });
    }
}
