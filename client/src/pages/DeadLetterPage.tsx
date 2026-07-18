import { useState } from 'react';
import { useWorkspace } from '../workspace/WorkspaceContext';
import { useAsync } from '../hooks/useAsync';
import { listDeadLetter, replayRun } from '../api/runs';
import { ApiError } from '../api/client';

export default function DeadLetterPage() {
  const { current, status, message } = useWorkspace();
  if (status === 'loading') return <p role="status">Loading workspace…</p>;
  if (status === 'error') return <p role="alert" className="error">{message}</p>;
  if (!current) return <p>No workspace available.</p>;
  return <DeadLetterInner workspaceId={current.id} />;
}

function DeadLetterInner({ workspaceId }: { workspaceId: string }) {
  const runs = useAsync(() => listDeadLetter(workspaceId, 1, 100).then((p) => p.items), [workspaceId]);
  const [fromStep, setFromStep] = useState<Record<string, number>>({});
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();

  async function replay(runId: string) {
    setError(undefined);
    setNotice(undefined);
    try {
      const run = await replayRun(workspaceId, runId, fromStep[runId] ?? 0);
      setNotice(`Replayed as run ${run.id} (${run.status}).`);
      runs.reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to replay run');
    }
  }

  return (
    <section>
      <h1>Dead-letter</h1>
      <p>Failed runs. Replay from a step to re-run after fixing the cause.</p>
      {error && <p role="alert" className="error">{error}</p>}
      {notice && <p role="status">{notice}</p>}

      {runs.status === 'loading' && <p role="status">Loading dead-letter…</p>}
      {runs.status === 'error' && <p role="alert" className="error">{runs.message}</p>}
      {runs.status === 'success' && (
        <table>
          <thead>
            <tr>
              <th>Flow</th>
              <th>Trigger</th>
              <th>Error</th>
              <th>From step</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {runs.data.map((r) => (
              <tr key={r.id}>
                <td>{r.flowName}</td>
                <td>{r.trigger}</td>
                <td>
                  <span className="badge status-failed">Failed</span>
                </td>
                <td>
                  <input
                    type="number"
                    min={0}
                    aria-label={`Replay ${r.flowName} from step`}
                    value={fromStep[r.id] ?? 0}
                    onChange={(e) => setFromStep((m) => ({ ...m, [r.id]: Number(e.target.value) }))}
                    style={{ width: '4rem' }}
                  />
                </td>
                <td>
                  <button type="button" onClick={() => replay(r.id)} aria-label={`Replay ${r.flowName}`}>
                    Replay
                  </button>
                </td>
              </tr>
            ))}
            {runs.data.length === 0 && (
              <tr>
                <td colSpan={5}>No failed runs. </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </section>
  );
}
