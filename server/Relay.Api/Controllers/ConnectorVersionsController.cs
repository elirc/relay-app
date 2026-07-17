using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Connectors;
using Relay.Api.Security;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>Manage a connector's schema versions: list, publish, deprecate.</summary>
[ApiController]
[Route("api/connectors/{connectorId:guid}/versions")]
public sealed class ConnectorVersionsController : ControllerBase
{
    private readonly RelayDbContext _db;

    public ConnectorVersionsController(RelayDbContext db) => _db = db;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ConnectorVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ConnectorVersionDto>>> List(Guid connectorId, CancellationToken ct)
    {
        if (!await ConnectorExists(connectorId, ct)) return ConnectorNotFound(connectorId);

        var versions = await _db.ConnectorVersions
            .AsNoTracking()
            .Where(v => v.ConnectorId == connectorId)
            .OrderBy(v => v.Version)
            .Select(v => ConnectorVersionDto.From(v))
            .ToListAsync(ct);
        return Ok(versions);
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ConnectorVersionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectorVersionDto>> Create(
        Guid connectorId,
        CreateConnectorVersionRequest request,
        CancellationToken ct)
    {
        var connector = await _db.Connectors
            .Include(c => c.Versions)
            .FirstOrDefaultAsync(c => c.Id == connectorId, ct);
        if (connector is null) return ConnectorNotFound(connectorId);

        var schema = string.IsNullOrWhiteSpace(request.ConfigSchemaJson) ? "{}" : request.ConfigSchemaJson;
        var nextVersion = (connector.Versions.Count > 0 ? connector.Versions.Max(v => v.Version) : 0) + 1;
        var version = new ConnectorVersion
        {
            Id = Guid.NewGuid(),
            ConnectorId = connectorId,
            Version = nextVersion,
            ConfigSchemaJson = schema,
            IsDeprecated = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        // Add through the DbSet: EF would otherwise treat a client-generated key
        // reached only via the tracked parent's collection as Modified, not Added.
        _db.ConnectorVersions.Add(version);
        connector.Versions.Add(version);
        // The connector's mirror schema tracks the newest version.
        connector.ConfigSchemaJson = schema;

        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { connectorId }, ConnectorVersionDto.From(version));
    }

    [HttpPost("{version:int}/deprecate")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ConnectorVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectorVersionDto>> Deprecate(Guid connectorId, int version, CancellationToken ct)
    {
        var row = await _db.ConnectorVersions
            .FirstOrDefaultAsync(v => v.ConnectorId == connectorId && v.Version == version, ct);
        if (row is null)
            return Problem(title: "Version not found",
                detail: $"Connector '{connectorId}' has no version {version}.",
                statusCode: StatusCodes.Status404NotFound);

        row.IsDeprecated = true;
        await _db.SaveChangesAsync(ct);
        return Ok(ConnectorVersionDto.From(row));
    }

    private Task<bool> ConnectorExists(Guid connectorId, CancellationToken ct) =>
        _db.Connectors.AnyAsync(c => c.Id == connectorId, ct);

    private ObjectResult ConnectorNotFound(Guid id) => Problem(
        title: "Connector not found", detail: $"No connector with id '{id}'.",
        statusCode: StatusCodes.Status404NotFound);
}
