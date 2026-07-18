import { useState } from 'react';
import { useAsync } from '../hooks/useAsync';
import { useWorkspace } from '../workspace/WorkspaceContext';
import { createConnection, deleteConnection, listConnections, rotateSecret } from '../api/connections';
import { listConnectors } from '../api/connectors';
import { ApiError } from '../api/client';
import { buildConfigJson, parseSchemaFields } from '../lib/schema';

export default function ConnectionsPage() {
  const { current, status: wsStatus, message: wsMessage } = useWorkspace();
  const workspaceId = current?.id;

  if (wsStatus === 'loading') return <p role="status">Loading workspace…</p>;
  if (wsStatus === 'error') {
    return (
      <p role="alert" className="error">
        {wsMessage}
      </p>
    );
  }
  if (!workspaceId) return <p>No workspace available.</p>;

  return <ConnectionsInner workspaceId={workspaceId} workspaceName={current!.name} />;
}

function ConnectionsInner({
  workspaceId,
  workspaceName,
}: {
  workspaceId: string;
  workspaceName: string;
}) {
  const connections = useAsync(
    () => listConnections(workspaceId, 1, 100).then((p) => p.items),
    [workspaceId],
  );
  const connectors = useAsync(() => listConnectors(1, 100).then((p) => p.items), []);

  const [connectorId, setConnectorId] = useState('');
  const [name, setName] = useState('');
  const [credentialsJson, setCredentialsJson] = useState('');
  const [configValues, setConfigValues] = useState<Record<string, string | boolean>>({});
  const [rawConfig, setRawConfig] = useState('{}');
  const [formError, setFormError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const availableConnectors = connectors.status === 'success' ? connectors.data : [];
  const selectedConnector = availableConnectors.find((c) => c.id === connectorId);
  const fields = selectedConnector ? parseSchemaFields(selectedConnector.configSchemaJson) : [];

  function onConnectorChange(id: string) {
    setConnectorId(id);
    setConfigValues({});
    setRawConfig('{}');
  }

  function resetForm() {
    setConnectorId('');
    setName('');
    setCredentialsJson('');
    setConfigValues({});
    setRawConfig('{}');
  }

  async function onCreate(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setFormError(undefined);
    const configJson = fields.length > 0 ? buildConfigJson(fields, configValues) : rawConfig;
    try {
      await createConnection(workspaceId, {
        connectorId,
        name,
        configJson,
        credentialsJson: credentialsJson.trim() ? credentialsJson : undefined,
      });
      resetForm();
      connections.reload();
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Failed to install connection');
    } finally {
      setBusy(false);
    }
  }

  async function onDelete(id: string) {
    try {
      await deleteConnection(workspaceId, id);
      connections.reload();
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Failed to delete connection');
    }
  }

  async function onRotate(id: string) {
    setFormError(undefined);
    try {
      await rotateSecret(workspaceId, id);
      connections.reload();
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Failed to rotate secret');
    }
  }

  return (
    <section>
      <h1>Connections</h1>
      <p>
        Installed connectors for workspace <strong>{workspaceName}</strong>.
      </p>

      {connections.status === 'loading' && <p role="status">Loading connections…</p>}
      {connections.status === 'error' && (
        <p role="alert" className="error">
          {connections.message}
        </p>
      )}
      {connections.status === 'success' && (
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Connector</th>
              <th>Version</th>
              <th>Status</th>
              <th>Credentials</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {connections.data.map((c) => (
              <tr key={c.id}>
                <td>{c.name}</td>
                <td>{c.connectorName}</td>
                <td>
                  {c.connectorVersion ? `v${c.connectorVersion}` : '—'}
                  {c.isVersionDeprecated && (
                    <span className="badge status-failed" style={{ marginLeft: '0.4rem' }}>
                      deprecated
                    </span>
                  )}
                </td>
                <td>{c.status}</td>
                <td>{c.hasCredentials ? 'set' : '—'}</td>
                <td className="actions">
                  {c.hasCredentials && (
                    <button
                      type="button"
                      onClick={() => onRotate(c.id)}
                      aria-label={`Rotate secret for ${c.name}`}
                    >
                      Rotate secret
                    </button>
                  )}
                  <button type="button" onClick={() => onDelete(c.id)} aria-label={`Delete ${c.name}`}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
            {connections.data.length === 0 && (
              <tr>
                <td colSpan={6}>No connections yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      )}

      <h2>Install a connection</h2>
      <form onSubmit={onCreate} className="stack">
        <label>
          Connector
          <select value={connectorId} onChange={(e) => onConnectorChange(e.target.value)} required>
            <option value="" disabled>
              Select a connector…
            </option>
            {availableConnectors.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
                {c.latestVersion ? ` (v${c.latestVersion})` : ''}
                {c.isLatestDeprecated ? ' — deprecated' : ''}
              </option>
            ))}
          </select>
        </label>
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} required />
        </label>

        {/* Schema-driven config: one field per connector property. */}
        {fields.length > 0
          ? fields.map((field) => (
              <label key={field.name}>
                {field.name}
                {field.required ? ' *' : ''}
                {field.type === 'boolean' ? (
                  <input
                    type="checkbox"
                    checked={Boolean(configValues[field.name])}
                    onChange={(e) =>
                      setConfigValues((v) => ({ ...v, [field.name]: e.target.checked }))
                    }
                  />
                ) : (
                  <input
                    type={field.type === 'integer' || field.type === 'number' ? 'number' : 'text'}
                    value={String(configValues[field.name] ?? '')}
                    required={field.required}
                    onChange={(e) =>
                      setConfigValues((v) => ({ ...v, [field.name]: e.target.value }))
                    }
                  />
                )}
              </label>
            ))
          : selectedConnector && (
              <label>
                Config (JSON)
                <input value={rawConfig} onChange={(e) => setRawConfig(e.target.value)} />
              </label>
            )}

        <label>
          Secret (JSON, optional — write-only)
          <input
            type="password"
            value={credentialsJson}
            onChange={(e) => setCredentialsJson(e.target.value)}
            placeholder='{"apiKey":"…"}'
            autoComplete="off"
          />
        </label>
        {formError && (
          <p role="alert" className="error">
            {formError}
          </p>
        )}
        <div>
          <button type="submit" disabled={busy}>
            {busy ? 'Installing…' : 'Install connection'}
          </button>
        </div>
      </form>
    </section>
  );
}
