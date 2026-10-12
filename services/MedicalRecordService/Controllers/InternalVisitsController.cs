using MedicalRecordService.Data;
using Microsoft.AspNetCore.Mvc;

namespace MedicalRecordService.Controllers;

// Available only inside the gateway-secret trust boundary; no public proxy route exposes it.
[ApiController]
[Route("internal/visits")]
public sealed class InternalVisitsController(IVisitContextRepository visits) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var visit = await visits.FindAsync(id, cancellationToken);
        return visit is null ? NotFound() : Ok(visit);
    }
}
