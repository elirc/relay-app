import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useWorkspace } from '../workspace/WorkspaceContext';
import { useAsync } from '../hooks/useAsync';
import { listConnections } from '../api/connections';
import { createFlow, getFlow, updateFlow } from '../api/flows';
import type { FlowInput, FlowStepInput } from '../api/flows';
import { createWebhook, deleteWebhook, listWebhooks } from '../api/webhooks';
import type { Connection } from '../api/types';
import { ApiError } from '../api/client';
import SchedulesSection from '../components/SchedulesSection';

export default function FlowEditorPage() {
  const { current, status, message } = useWorkspace();
  const { id } = useParams();
  if (status === 'loading') return <p role="status">Loading workspace…</p>;
  if (status === 'error') return <p role="alert" className="error">{message}</p>;
  if (!current) return <p>No workspace available.</p>;
  return <Editor workspaceId={current.id} flowId={id} key={id ?? 'new'} />;
}

const emptyStep: FlowStepInput = {
  name: '',
  connectionId: '',
  action: '',
  configJson: '{}',
  maxAttempts: 3,
  backoffSeconds: 0,
};

function Editor({ workspaceId, flowId }: { workspaceId: string; flowId?: string }) {
  const navigate = useNavigate();
  const isEdit = Boolean(flowId);

  const connections = useAsync(
    () => listConnections(workspaceId, 1, 100).then((p) => p.items),
    [workspaceId],
  );
  const flow = useAsync(
    () => (flowId ? getFlow(workspaceId, flowId) : Promise.resolve(null)),
    [workspaceId, flowId],
  );

  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [triggerConnectionId, setTriggerConnectionId] = useState('');
  const [steps, setSteps] = useState<FlowStepInput[]>([{ ...emptyStep }]);
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);

  // Populate once from the loaded flow in edit mode.
  useEffect(() => {
    if (!isEdit || loaded || flow.status !== 'success' || !flow.data) return;
    const f = flow.data;
    setName(f.name);
    setDescription(f.description ?? '');
    setTriggerConnectionId(f.triggerConnectionId);
    setSteps(
      f.steps.map((s) => ({
        name: s.name,
        connectionId: s.connectionId,
        action: s.action,
        configJson: s.configJson,
        maxAttempts: s.maxAttempts,
        backoffSeconds: s.backoffSeconds,
      })),
    );
    setLoaded(true);
  }, [isEdit, loaded, flow]);

  function updateStep(index: number, patch: Partial<FlowStepInput>) {
    setSteps((prev) => prev.map((s, i) => (i === index ? { ...s, ...patch } : s)));
  }

  function addStep() {
    setSteps((prev) => [...prev, { ...emptyStep }]);
  }

  function removeStep(index: number) {
    setSteps((prev) => prev.filter((_, i) => i !== index));
  }

  function move(index: number, delta: number) {
    setSteps((prev) => {
      const next = [...prev];
      const target = index + delta;
      if (target < 0 || target >= next.length) return prev;
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(undefined);
    const input: FlowInput = { name, description: description || null, triggerConnectionId, steps };
    try {
      if (isEdit && flowId) {
        await updateFlow(workspaceId, flowId, input);
      } else {
        await createFlow(workspaceId, input);
      }
      navigate('/flows');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to save flow');
    } finally {
      setBusy(false);
    }
  }

  if (connections.status === 'loading' || (isEdit && flow.status === 'loading')) {
    return <p role="status">Loading…</p>;
  }
  if (connections.status === 'error') {
    return <p role="alert" className="error">{connections.message}</p>;
  }

  const options: Connection[] = connections.status === 'success' ? connections.data : [];

  return (
    <section>
      <h1>{isEdit ? 'Edit flow' : 'New flow'}</h1>
      <form onSubmit={onSubmit} className="stack">
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} required />
        </label>
        <label>
          Description
          <input value={description} onChange={(e) => setDescription(e.target.value)} />
        </label>
        <label>
          Trigger connection
          <select
            value={triggerConnectionId}
            onChange={(e) => setTriggerConnectionId(e.target.value)}
            required
          >
            <option value="" disabled>
              Select a trigger…
            </option>
            {options.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>

        <h2>Steps</h2>
        {steps.map((step, index) => (
          <fieldset key={index} className="step-card">
            <legend>Step {index + 1}</legend>
            <label>
              Name
              <input
                value={step.name}
                onChange={(e) => updateStep(index, { name: e.target.value })}
                required
              />
            </label>
            <label>
              Connection
              <select
                value={step.connectionId}
                onChange={(e) => updateStep(index, { connectionId: e.target.value })}
                required
              >
                <option value="" disabled>
                  Select a connection…
                </option>
                {options.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Action
              <input
                value={step.action}
                onChange={(e) => updateStep(index, { action: e.target.value })}
                placeholder="e.g. send_message"
                required
              />
            </label>
            <label>
              Max attempts
              <input
                type="number"
                min={1}
                max={10}
                value={step.maxAttempts ?? 3}
                onChange={(e) => updateStep(index, { maxAttempts: Number(e.target.value) })}
              />
            </label>
            <label>
              Backoff (s)
              <input
                type="number"
                min={0}
                max={3600}
                value={step.backoffSeconds ?? 0}
                onChange={(e) => updateStep(index, { backoffSeconds: Number(e.target.value) })}
              />
            </label>
            <div className="actions">
              <button type="button" onClick={() => move(index, -1)} disabled={index === 0}>
                Up
              </button>
              <button
                type="button"
                onClick={() => move(index, 1)}
                disabled={index === steps.length - 1}
              >
                Down
              </button>
              <button
                type="button"
                onClick={() => removeStep(index)}
                disabled={steps.length === 1}
                aria-label={`Remove step ${index + 1}`}
              >
                Remove
              </button>
            </div>
          </fieldset>
        ))}
        <div>
          <button type="button" onClick={addStep}>
            Add step
          </button>
        </div>

        {error && <p role="alert" className="error">{error}</p>}
        <div className="actions">
          <button type="submit" disabled={busy}>
            {busy ? 'Saving…' : isEdit ? 'Save changes' : 'Create flow'}
          </button>
          <button type="button" onClick={() => navigate('/flows')}>
            Cancel
          </button>
        </div>
      </form>

      {isEdit && flowId && (
        <>
          <SchedulesSection workspaceId={workspaceId} flowId={flowId} />
          <WebhooksSection workspaceId={workspaceId} flowId={flowId} />
        </>
      )}
    </section>
  );
}

function WebhooksSection({ workspaceId, flowId }: { workspaceId: string; flowId: string }) {
  const webhooks = useAsync(() => listWebhooks(workspaceId, flowId), [workspaceId, flowId]);
  const [error, setError] = useState<string>();

  async function add() {
    setError(undefined);
    try {
      await createWebhook(workspaceId, flowId);
      webhooks.reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to create webhook');
    }
  }

  async function remove(id: string) {
    setError(undefined);
    try {
      await deleteWebhook(workspaceId, flowId, id);
      webhooks.reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to delete webhook');
    }
  }

  return (
    <div className="webhooks">
      <div className="row-between">
        <h2>Inbound webhooks</h2>
        <button type="button" onClick={add}>
          Add webhook
        </button>
      </div>
      <p>POST to a webhook URL to trigger this flow (the flow must be enabled).</p>
      {error && <p role="alert" className="error">{error}</p>}
      {webhooks.status === 'loading' && <p role="status">Loading webhooks…</p>}
      {webhooks.status === 'error' && <p role="alert" className="error">{webhooks.message}</p>}
      {webhooks.status === 'success' && (
        <table>
          <thead>
            <tr>
              <th>URL</th>
              <th>State</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {webhooks.data.map((w) => (
              <tr key={w.id}>
                <td>
                  <code>{w.url}</code>
                </td>
                <td>{w.isEnabled ? 'Enabled' : 'Disabled'}</td>
                <td>
                  <button type="button" onClick={() => remove(w.id)} aria-label={`Delete webhook ${w.token}`}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
            {webhooks.data.length === 0 && (
              <tr>
                <td colSpan={3}>No webhooks yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
