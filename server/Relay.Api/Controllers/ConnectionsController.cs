using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Connections;
using Relay.Api.Security;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Domain.Security;
using Relay.Domain.Validation;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>CRUD for connections (installed connector instances) within a workspace.</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/connections")]
public sealed class ConnectionsController : ControllerBase
{
    private readonly RelayDbContext _db;
    private readonly ISecretProtector _protector;

    public ConnectionsController(RelayDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    /// <summary>Encrypts a submitted secret, or null when the caller sends nothing/clears it.</summary>
    private string? ProtectOrNull(string? plaintext) =>
        string.IsNullOrWhiteSpace(plaintext) || plaintext == "{}" ? null : _protector.Protect(plaintext);

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
            .Include(c => c.ConnectorVersion)
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
            .Include(c => c.ConnectorVersion)
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

        var connector = await _db.Connectors
            .Include(c => c.Versions)
            .FirstOrDefaultAsync(c => c.Id == request.ConnectorId!.Value, ct);
        if (connector is null)
        {
            ModelState.AddModelError(nameof(request.ConnectorId), "Unknown connector.");
            return ValidationProblem(ModelState);
        }

        // Resolve the target schema version: an explicit request must exist and
        // must not be deprecated; otherwise default to the latest live version.
        ConnectorVersion? version;
        if (request.ConnectorVersion is int requested)
        {
            version = connector.Versions.FirstOrDefault(v => v.Version == requested);
            if (version is null)
            {
                ModelState.AddModelError(nameof(request.ConnectorVersion), "Unknown connector version.");
                return ValidationProblem(ModelState);
            }
            if (version.IsDeprecated)
            {
                ModelState.AddModelError(nameof(request.ConnectorVersion),
                    "This connector version is deprecated; install a newer version.");
                return ValidationProblem(ModelState);
            }
        }
        else
        {
            version = connector.Versions.Where(v => !v.IsDeprecated).OrderByDescending(v => v.Version).FirstOrDefault()
                ?? connector.Versions.OrderByDescending(v => v.Version).FirstOrDefault();
        }

        var configJson = string.IsNullOrWhiteSpace(request.ConfigJson) ? "{}" : request.ConfigJson;
        var schema = version?.ConfigSchemaJson ?? connector.ConfigSchemaJson;
        var schemaErrors = JsonSchemaValidator.Validate(schema, configJson);
        if (schemaErrors.Count > 0)
        {
            foreach (var error in schemaErrors) ModelState.AddModelError(nameof(request.ConfigJson), error);
            return ValidationProblem(ModelState);
        }

        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ConnectorId = connector.Id,
            ConnectorVersionId = version?.Id,
            Name = request.Name!.Trim(),
            ConfigJson = configJson,
            EncryptedSecret = ProtectOrNull(request.CredentialsJson),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _db.Connections.Add(connection);
        await _db.SaveChangesAsync(ct);

        connection.Connector = connector;
        connection.ConnectorVersion = version;
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
            .Include(c => c.ConnectorVersion)
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId, ct);
        if (connection is null) return ConnectionNotFound(id);

        var configJson = string.IsNullOrWhiteSpace(request.ConfigJson) ? "{}" : request.ConfigJson;
        var schema = connection.ConnectorVersion?.ConfigSchemaJson ?? connection.Connector?.ConfigSchemaJson ?? "{}";
        var schemaErrors = JsonSchemaValidator.Validate(schema, configJson);
        if (schemaErrors.Count > 0)
        {
            foreach (var error in schemaErrors) ModelState.AddModelError(nameof(request.ConfigJson), error);
            return ValidationProblem(ModelState);
        }

        connection.Name = request.Name!.Trim();
        connection.ConfigJson = configJson;
        connection.Status = request.Status!.Value;
        // Only touch the secret when supplied: a value re-seals it, "{}"/empty clears it.
        if (request.CredentialsJson is not null)
        {
            connection.EncryptedSecret = ProtectOrNull(request.CredentialsJson);
        }
        connection.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(ConnectionDto.From(connection));
    }

    /// <summary>Re-encrypts the stored secret under a brand-new data key.</summary>
    [HttpPost("{id:guid}/rotate-secret")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ConnectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectionDto>> RotateSecret(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var connection = await _db.Connections
            .Include(c => c.Connector)
            .Include(c => c.ConnectorVersion)
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == workspaceId, ct);
        if (connection is null) return ConnectionNotFound(id);

        if (string.IsNullOrWhiteSpace(connection.EncryptedSecret))
        {
            return Problem(
                title: "No secret to rotate",
                detail: "This connection has no stored credentials.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        connection.EncryptedSecret = _protector.Rotate(connection.EncryptedSecret);
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
