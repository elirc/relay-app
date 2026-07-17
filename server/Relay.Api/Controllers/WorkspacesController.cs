using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Workspaces;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>Read-only workspace directory used by the client to scope resources.</summary>
[ApiController]
[Route("api/workspaces")]
public sealed class WorkspacesController : ControllerBase
{
    private readonly RelayDbContext _db;

    public WorkspacesController(RelayDbContext db) => _db = db;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WorkspaceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceDto>>> List(CancellationToken ct)
    {
        var workspaces = await _db.Workspaces
            .AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => WorkspaceDto.From(w))
            .ToListAsync(ct);
        return Ok(workspaces);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkspaceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkspaceDto>> Get(Guid id, CancellationToken ct)
    {
        var workspace = await _db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        return workspace is null
            ? Problem(title: "Workspace not found", detail: $"No workspace with id '{id}'.", statusCode: StatusCodes.Status404NotFound)
            : Ok(WorkspaceDto.From(workspace));
    }
}
