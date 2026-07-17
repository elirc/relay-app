using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Connections;
using Relay.Api.Security;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>CRUD for connections (installed connector instances) within a workspace.</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/connections")]
public sealed class ConnectionsController : ControllerBase
{
    private readonly RelayDbContext _db;

    public ConnectionsController(RelayDbContext db) => _db = db;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ConnectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ConnectionDto>>> List(
        Guid workspaceId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (!await WorkspaceExists(workspaceId, ct)) return WorkspaceNotFound(workspaceId);

        // Include before Skip/Take (EF gotcha) — ToPagedResultAsync applies paging last.
        var result = await _db.Connections
            .AsNoTracking()
            .Include(c => c.Connector)
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderBy(c => c.Name)
            .ToPagedResultAsync(pagination, ConnectionDto.From, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ConnectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectionDto>> Get(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var connection = await _db.Connections
            .AsNoTracking()
            .Include(c => c.Connector)
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId, ct);
        return connection is null ? ConnectionNotFound(id) : Ok(ConnectionDto.From(connection));
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ConnectionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectionDto>> Create(
        Guid workspaceId,
        CreateConnectionRequest request,
        CancellationToken ct)
    {
        if (!await WorkspaceExists(workspaceId, ct)) return WorkspaceNotFound(workspaceId);

        var connector = await _db.Connectors.FirstOrDefaultAsync(c => c.Id == request.ConnectorId!.Value, ct);
        if (connector is null)
        {
            ModelState.AddModelError(nameof(request.ConnectorId), "Unknown connector.");
            return ValidationProblem(ModelState);
        }

        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ConnectorId = connector.Id,
            Name = request.Name!.Trim(),
            ConfigJson = string.IsNullOrWhiteSpace(request.ConfigJson) ? "{}" : request.ConfigJson,
            CredentialsJson = string.IsNullOrWhiteSpace(request.CredentialsJson) ? "{}" : request.CredentialsJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _db.Connections.Add(connection);
        await _db.SaveChangesAsync(ct);

        connection.Connector = connector;
        return CreatedAtAction(nameof(Get), new { workspaceId, id = connection.Id }, ConnectionDto.From(connection));
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ConnectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectionDto>> Update(
        Guid workspaceId,
        Guid id,
        UpdateConnectionRequest request,
        CancellationToken ct)
    {
        var connection = await _db.Connections
            .Include(c => c.Connector)
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId, ct);
        if (connection is null) return ConnectionNotFound(id);

        connection.Name = request.Name!.Trim();
        connection.ConfigJson = string.IsNullOrWhiteSpace(request.ConfigJson) ? "{}" : request.ConfigJson;
        connection.Status = request.Status!.Value;
        // Only overwrite credentials when the caller supplies them.
        if (request.CredentialsJson is not null)
        {
            connection.CredentialsJson = request.CredentialsJson;
        }
        connection.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(ConnectionDto.From(connection));
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var connection = await _db.Connections
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId, ct);
        if (connection is null) return ConnectionNotFound(id);

        var usedByFlow = await _db.Flows.AnyAsync(f => f.TriggerConnectionId == id, ct)
            || await _db.FlowSteps.AnyAsync(s => s.ConnectionId == id, ct);
        if (usedByFlow)
        {
            return Problem(
                title: "Connection in use",
                detail: "This connection is referenced by a flow trigger or step and cannot be deleted.",
                statusCode: StatusCodes.Status409Conflict);
        }

        _db.Connections.Remove(connection);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<bool> WorkspaceExists(Guid workspaceId, CancellationToken ct) =>
        _db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct);

    private ObjectResult WorkspaceNotFound(Guid id) => Problem(
        title: "Workspace not found",
        detail: $"No workspace with id '{id}'.",
        statusCode: StatusCodes.Status404NotFound);

    private ObjectResult ConnectionNotFound(Guid id) => Problem(
        title: "Connection not found",
        detail: $"No connection with id '{id}'.",
        statusCode: StatusCodes.Status404NotFound);
}
