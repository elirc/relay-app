import { Link, useNavigate } from 'react-router-dom';
import { useAsync } from '../hooks/useAsync';
import { useWorkspace } from '../workspace/WorkspaceContext';
import { deleteFlow, disableFlow, enableFlow, exportFlow, importFlow, listFlows } from '../api/flows';
import type { FlowExport, ImportResult } from '../api/flows';
import { runFlow } from '../api/runs';
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
  const navigate = useNavigate();

  async function run(id: string) {
    setError(undefined);
    try {
      await runFlow(workspaceId, id);
      navigate('/runs');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to run flow');
    }
  }

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

  const [exported, setExported] = useState<string>();

  async function doExport(id: string) {
    setError(undefined);
    try {
      const doc = await exportFlow(workspaceId, id);
      setExported(JSON.stringify(doc, null, 2));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to export flow');
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
                  <button type="button" onClick={() => run(f.id)} aria-label={`Run ${f.name}`}>
                    Run now
                  </button>
                  <button type="button" onClick={() => toggle(f.id, f.isEnabled)}>
                    {f.isEnabled ? 'Disable' : 'Enable'}
                  </button>
                  <button type="button" onClick={() => remove(f.id)} aria-label={`Delete ${f.name}`}>
                    Delete
                  </button>
                  <button type="button" onClick={() => doExport(f.id)} aria-label={`Export ${f.name}`}>
                    Export
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

      {exported && (
        <div className="port-panel">
          <div className="row-between">
            <h2>Exported flow (JSON)</h2>
            <button type="button" onClick={() => setExported(undefined)}>
              Close
            </button>
          </div>
          <textarea readOnly aria-label="Exported flow JSON" value={exported} rows={12} />
        </div>
      )}

      <ImportPanel workspaceId={workspaceId} onImported={() => flows.reload()} />
    </section>
  );
}

function ImportPanel({
  workspaceId,
  onImported,
}: {
  workspaceId: string;
  onImported: () => void;
}) {
  const [text, setText] = useState('');
  const [result, setResult] = useState<ImportResult>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  function parse(): FlowExport | undefined {
    try {
      return JSON.parse(text) as FlowExport;
    } catch {
      setError('Invalid JSON.');
      return undefined;
    }
  }

  async function validate() {
    setError(undefined);
    setResult(undefined);
    const doc = parse();
    if (!doc) return;
    setBusy(true);
    try {
      setResult(await importFlow(workspaceId, doc, true));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Validation failed');
    } finally {
      setBusy(false);
    }
  }

  async function runImport() {
    setError(undefined);
    const doc = parse();
    if (!doc) return;
    setBusy(true);
    try {
      const res = await importFlow(workspaceId, doc, false);
      setResult(res);
      if (res.valid) {
        setText('');
        onImported();
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Import failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="port-panel">
      <h2>Import a flow</h2>
      <p>Paste an exported flow JSON, validate it, then import. Re-importing the same document updates in place.</p>
      <textarea
        aria-label="Import flow JSON"
        value={text}
        onChange={(e) => setText(e.target.value)}
        rows={8}
        placeholder='{"externalId":"…","name":"…","trigger":{…},"steps":[…]}'
      />
      {error && <p role="alert" className="error">{error}</p>}
      {result && (
        <div role="status" className={result.valid ? '' : 'error'}>
          {result.valid ? `Valid — will ${result.action}.` : 'Invalid:'}
          {result.issues.length > 0 && (
            <ul>
              {result.issues.map((i) => (
                <li key={i}>{i}</li>
              ))}
            </ul>
          )}
        </div>
      )}
      <div className="actions">
        <button type="button" onClick={validate} disabled={busy || !text.trim()}>
          Validate
        </button>
        <button type="button" onClick={runImport} disabled={busy || !result?.valid}>
          Import
        </button>
      </div>
    </div>
  );
}
