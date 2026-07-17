using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Workspaces;
using Relay.Api.Security;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>
/// Workspace directory, scoped to the caller: a user only ever sees their own
/// workspace, so a foreign id reads as not-found.
/// </summary>
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
        var callerWorkspaceId = User.GetWorkspaceId();
        var workspaces = await _db.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == callerWorkspaceId)
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
        var workspace = id == User.GetWorkspaceId()
            ? await _db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct)
            : null;
        return workspace is null
            ? Problem(title: "Workspace not found", detail: $"No workspace with id '{id}'.", statusCode: StatusCodes.Status404NotFound)
            : Ok(WorkspaceDto.From(workspace));
    }
}
