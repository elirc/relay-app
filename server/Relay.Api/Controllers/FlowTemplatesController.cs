using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Flows;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>The global gallery of predefined flow templates.</summary>
[ApiController]
[Route("api/flow-templates")]
public sealed class FlowTemplatesController : ControllerBase
{
    private readonly RelayDbContext _db;

    public FlowTemplatesController(RelayDbContext db) => _db = db;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FlowTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FlowTemplateDto>>> List(CancellationToken ct)
    {
        var templates = await _db.FlowTemplates
            .AsNoTracking()
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .ToListAsync(ct);
        return Ok(templates.Select(FlowTemplateDto.From).ToList());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FlowTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowTemplateDto>> Get(Guid id, CancellationToken ct)
    {
        var template = await _db.FlowTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return template is null
            ? Problem(title: "Template not found", detail: $"No template with id '{id}'.", statusCode: StatusCodes.Status404NotFound)
            : Ok(FlowTemplateDto.From(template));
    }
}
