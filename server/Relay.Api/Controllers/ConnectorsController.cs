using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Connectors;
using Relay.Api.Security;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>CRUD for the global connector catalog. Reads are open to any member;
/// catalog mutations require Admin.</summary>
[ApiController]
[Route("api/connectors")]
public sealed class ConnectorsController : ControllerBase
{
    private readonly RelayDbContext _db;

    public ConnectorsController(RelayDbContext db) => _db = db;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ConnectorDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ConnectorDto>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        var result = await _db.Connectors
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToPagedResultAsync(pagination, ConnectorDto.From, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ConnectorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectorDto>> Get(Guid id, CancellationToken ct)
    {
        var connector = await _db.Connectors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return connector is null ? NotFoundProblem(id) : Ok(ConnectorDto.From(connector));
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ConnectorDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConnectorDto>> Create(CreateConnectorRequest request, CancellationToken ct)
    {
        var key = request.Key!.Trim();
        if (await _db.Connectors.AnyAsync(c => c.Key == key, ct))
        {
            return Problem(
                title: "Connector key already exists",
                detail: $"A connector with key '{key}' already exists.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var connector = new Connector
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = request.Name!.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            AuthKind = request.AuthKind!.Value,
            ConfigSchemaJson = string.IsNullOrWhiteSpace(request.ConfigSchemaJson) ? "{}" : request.ConfigSchemaJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        _db.Connectors.Add(connector);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = connector.Id }, ConnectorDto.From(connector));
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ConnectorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectorDto>> Update(Guid id, UpdateConnectorRequest request, CancellationToken ct)
    {
        var connector = await _db.Connectors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connector is null) return NotFoundProblem(id);

        connector.Name = request.Name!.Trim();
        connector.Description = request.Description?.Trim() ?? string.Empty;
        connector.AuthKind = request.AuthKind!.Value;
        connector.ConfigSchemaJson = string.IsNullOrWhiteSpace(request.ConfigSchemaJson) ? "{}" : request.ConfigSchemaJson;

        await _db.SaveChangesAsync(ct);
        return Ok(ConnectorDto.From(connector));
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var connector = await _db.Connectors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connector is null) return NotFoundProblem(id);

        if (await _db.Connections.AnyAsync(c => c.ConnectorId == id, ct))
        {
            return Problem(
                title: "Connector in use",
                detail: "This connector still has installed connections and cannot be deleted.",
                statusCode: StatusCodes.Status409Conflict);
        }

        _db.Connectors.Remove(connector);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private ObjectResult NotFoundProblem(Guid id) => Problem(
        title: "Connector not found",
        detail: $"No connector with id '{id}'.",
        statusCode: StatusCodes.Status404NotFound);
}
