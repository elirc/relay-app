import { useState } from 'react';
import { useAsync } from '../hooks/useAsync';
import { useWorkspace } from '../workspace/WorkspaceContext';
import { createConnection, deleteConnection, listConnections } from '../api/connections';
import type { CreateConnectionInput } from '../api/connections';
import { listConnectors } from '../api/connectors';
import { ApiError } from '../api/client';

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

  const [form, setForm] = useState<CreateConnectionInput>({
    connectorId: '',
    name: '',
    configJson: '{}',
    credentialsJson: '',
  });
  const [formError, setFormError] = useState<string>();
  const [busy, setBusy] = useState(false);

  async function onCreate(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setFormError(undefined);
    try {
      await createConnection(workspaceId, {
        ...form,
        credentialsJson: form.credentialsJson?.trim() ? form.credentialsJson : undefined,
      });
      setForm({ connectorId: '', name: '', configJson: '{}', credentialsJson: '' });
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
                <td>{c.status}</td>
                <td>{c.hasCredentials ? 'set' : '—'}</td>
                <td>
                  <button type="button" onClick={() => onDelete(c.id)} aria-label={`Delete ${c.name}`}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
            {connections.data.length === 0 && (
              <tr>
                <td colSpan={5}>No connections yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      )}

      <h2>Install a connection</h2>
      <form onSubmit={onCreate} className="stack">
        <label>
          Connector
          <select
            value={form.connectorId}
            onChange={(e) => setForm({ ...form, connectorId: e.target.value })}
            required
          >
            <option value="" disabled>
              Select a connector…
            </option>
            {connectors.status === 'success' &&
              connectors.data.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
          </select>
        </label>
        <label>
          Name
          <input
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
            required
          />
        </label>
        <label>
          Config (JSON)
          <input
            value={form.configJson}
            onChange={(e) => setForm({ ...form, configJson: e.target.value })}
          />
        </label>
        <label>
          Credentials (JSON, optional)
          <input
            value={form.credentialsJson}
            onChange={(e) => setForm({ ...form, credentialsJson: e.target.value })}
            placeholder='{"apiKey":"…"}'
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
