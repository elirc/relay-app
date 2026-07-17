import { useState } from 'react';
import { useWorkspace } from '../workspace/WorkspaceContext';
import { useAsync } from '../hooks/useAsync';
import { getRun, listRuns, retryRun } from '../api/runs';
import { ApiError } from '../api/client';

export default function RunsPage() {
  const { current, status, message } = useWorkspace();
  if (status === 'loading') return <p role="status">Loading workspace…</p>;
  if (status === 'error') return <p role="alert" className="error">{message}</p>;
  if (!current) return <p>No workspace available.</p>;
  return <RunsInner workspaceId={current.id} />;
}

function fmtDuration(ms: number): string {
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(2)} s`;
}

function RunsInner({ workspaceId }: { workspaceId: string }) {
  const runs = useAsync(() => listRuns(workspaceId, 1, 100).then((p) => p.items), [workspaceId]);
  const [selectedId, setSelectedId] = useState<string>();
  const [error, setError] = useState<string>();

  async function onRetry(id: string) {
    setError(undefined);
    try {
      const run = await retryRun(workspaceId, id);
      runs.reload();
      setSelectedId(run.id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to retry run');
    }
  }

  return (
    <section>
      <h1>Runs</h1>
      <p>Execution history for this workspace.</p>
      {error && <p role="alert" className="error">{error}</p>}

      {runs.status === 'loading' && <p role="status">Loading runs…</p>}
      {runs.status === 'error' && <p role="alert" className="error">{runs.message}</p>}
      {runs.status === 'success' && (
        <table>
          <thead>
            <tr>
              <th>Flow</th>
              <th>Status</th>
              <th>Trigger</th>
              <th>Duration</th>
              <th>Retries</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {runs.data.map((r) => (
              <tr key={r.id}>
                <td>{r.flowName}</td>
                <td>
                  <span className={`badge status-${r.status.toLowerCase()}`}>{r.status}</span>
                </td>
                <td>{r.trigger}</td>
                <td>{fmtDuration(r.durationMs)}</td>
                <td>{r.retryCount}</td>
                <td className="actions">
                  <button type="button" onClick={() => setSelectedId(r.id)}>
                    View
                  </button>
                  <button type="button" onClick={() => onRetry(r.id)}>
                    Retry
                  </button>
                </td>
              </tr>
            ))}
            {runs.data.length === 0 && (
              <tr>
                <td colSpan={6}>No runs yet. Trigger a flow to see history here.</td>
              </tr>
            )}
          </tbody>
        </table>
      )}

      {selectedId && (
        <RunDetailView workspaceId={workspaceId} runId={selectedId} onClose={() => setSelectedId(undefined)} />
      )}
    </section>
  );
}

function RunDetailView({
  workspaceId,
  runId,
  onClose,
}: {
  workspaceId: string;
  runId: string;
  onClose: () => void;
}) {
  const run = useAsync(() => getRun(workspaceId, runId), [workspaceId, runId]);

  return (
    <div className="run-detail">
      <div className="row-between">
        <h2>Run detail</h2>
        <button type="button" onClick={onClose}>
          Close
        </button>
      </div>
      {run.status === 'loading' && <p role="status">Loading run…</p>}
      {run.status === 'error' && <p role="alert" className="error">{run.message}</p>}
      {run.status === 'success' && (
        <>
          {run.data.error && <p className="error">{run.data.error}</p>}
          <table>
            <thead>
              <tr>
                <th>#</th>
                <th>Step</th>
                <th>Status</th>
                <th>Message</th>
                <th>Duration</th>
              </tr>
            </thead>
            <tbody>
              {run.data.stepLogs.map((log) => (
                <tr key={log.id}>
                  <td>{log.stepOrder}</td>
                  <td>{log.name}</td>
                  <td>
                    <span className={`badge status-${log.status.toLowerCase()}`}>{log.status}</span>
                  </td>
                  <td>{log.message}</td>
                  <td>{fmtDuration(log.durationMs)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </div>
  );
}
