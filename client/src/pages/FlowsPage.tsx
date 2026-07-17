import { Link } from 'react-router-dom';
import { useAsync } from '../hooks/useAsync';
import { useWorkspace } from '../workspace/WorkspaceContext';
import { deleteFlow, disableFlow, enableFlow, listFlows } from '../api/flows';
import { ApiError } from '../api/client';
import { useState } from 'react';

export default function FlowsPage() {
  const { current, status, message } = useWorkspace();
  if (status === 'loading') return <p role="status">Loading workspace…</p>;
  if (status === 'error') return <p role="alert" className="error">{message}</p>;
  if (!current) return <p>No workspace available.</p>;
  return <FlowsInner workspaceId={current.id} />;
}

function FlowsInner({ workspaceId }: { workspaceId: string }) {
  const flows = useAsync(() => listFlows(workspaceId, 1, 100).then((p) => p.items), [workspaceId]);
  const [error, setError] = useState<string>();

  async function toggle(id: string, isEnabled: boolean) {
    setError(undefined);
    try {
      await (isEnabled ? disableFlow : enableFlow)(workspaceId, id);
      flows.reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to toggle flow');
    }
  }

  async function remove(id: string) {
    setError(undefined);
    try {
      await deleteFlow(workspaceId, id);
      flows.reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to delete flow');
    }
  }

  return (
    <section>
      <div className="row-between">
        <h1>Flows</h1>
        <Link to="/flows/new">
          <button type="button">New flow</button>
        </Link>
      </div>

      {error && <p role="alert" className="error">{error}</p>}
      {flows.status === 'loading' && <p role="status">Loading flows…</p>}
      {flows.status === 'error' && <p role="alert" className="error">{flows.message}</p>}
      {flows.status === 'success' && (
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Trigger</th>
              <th>Steps</th>
              <th>State</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {flows.data.map((f) => (
              <tr key={f.id}>
                <td>
                  <Link to={`/flows/${f.id}`}>{f.name}</Link>
                </td>
                <td>{f.triggerConnectionName}</td>
                <td>{f.stepCount}</td>
                <td>
                  <span className="badge">{f.isEnabled ? 'Enabled' : 'Disabled'}</span>
                </td>
                <td className="actions">
                  <button type="button" onClick={() => toggle(f.id, f.isEnabled)}>
                    {f.isEnabled ? 'Disable' : 'Enable'}
                  </button>
                  <button type="button" onClick={() => remove(f.id)} aria-label={`Delete ${f.name}`}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
            {flows.data.length === 0 && (
              <tr>
                <td colSpan={5}>No flows yet. Create one to get started.</td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </section>
  );
}
