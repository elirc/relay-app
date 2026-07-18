import { useState } from 'react';
import { useAsync } from '../hooks/useAsync';
import {
  createWebhook,
  deleteSigningSecret,
  deleteWebhook,
  generateSigningSecret,
  listDeliveries,
  listWebhooks,
} from '../api/webhooks';
import type { SigningSecretResponse } from '../api/webhooks';
import type { WebhookDelivery } from '../api/types';
import { ApiError } from '../api/client';

/** Manage a flow's inbound webhooks: create/delete, signing-secret (show-once), and delivery log. */
export default function WebhooksSection({
  workspaceId,
  flowId,
}: {
  workspaceId: string;
  flowId: string;
}) {
  const webhooks = useAsync(() => listWebhooks(workspaceId, flowId), [workspaceId, flowId]);
  const [error, setError] = useState<string>();
  const [revealed, setRevealed] = useState<Record<string, SigningSecretResponse>>({});
  const [deliveriesFor, setDeliveriesFor] = useState<string>();

  async function guard(fn: () => Promise<void>, fallback: string) {
    setError(undefined);
    try {
      await fn();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : fallback);
    }
  }

  const add = () =>
    guard(async () => {
      await createWebhook(workspaceId, flowId);
      webhooks.reload();
    }, 'Failed to create webhook');

  const remove = (id: string) =>
    guard(async () => {
      await deleteWebhook(workspaceId, flowId, id);
      webhooks.reload();
    }, 'Failed to delete webhook');

  const generate = (id: string) =>
    guard(async () => {
      const secret = await generateSigningSecret(workspaceId, flowId, id);
      setRevealed((r) => ({ ...r, [id]: secret }));
      webhooks.reload();
    }, 'Failed to generate signing secret');

  const disableSigning = (id: string) =>
    guard(async () => {
      await deleteSigningSecret(workspaceId, flowId, id);
      setRevealed((r) => {
        const next = { ...r };
        delete next[id];
        return next;
      });
      webhooks.reload();
    }, 'Failed to disable signing');

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
              <th>Signing</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {webhooks.data.map((w) => (
              <tr key={w.id}>
                <td>
                  <code>{w.url}</code>
                  {revealed[w.id] && (
                    <div className="signing-secret" role="status">
                      <strong>Signing secret (shown once):</strong>{' '}
                      <code>{revealed[w.id].signingSecret}</code>
                      <div className="hint">
                        Sign <code>{'{timestamp}.{body}'}</code> with HMAC-SHA256 and send{' '}
                        <code>{revealed[w.id].timestampHeader}</code> +{' '}
                        <code>{revealed[w.id].signatureHeader}</code>.
                      </div>
                    </div>
                  )}
                </td>
                <td>
                  <span className="badge">{w.requireSignature ? 'Required' : 'Off'}</span>
                </td>
                <td className="actions">
                  <button type="button" onClick={() => generate(w.id)}>
                    {w.hasSigningSecret ? 'Rotate secret' : 'Enable signing'}
                  </button>
                  {w.hasSigningSecret && (
                    <button type="button" onClick={() => disableSigning(w.id)}>
                      Disable signing
                    </button>
                  )}
                  <button
                    type="button"
                    onClick={() => setDeliveriesFor((cur) => (cur === w.id ? undefined : w.id))}
                  >
                    Deliveries
                  </button>
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

      {deliveriesFor && (
        <DeliveryLog workspaceId={workspaceId} flowId={flowId} webhookId={deliveriesFor} />
      )}
    </div>
  );
}

function DeliveryLog({
  workspaceId,
  flowId,
  webhookId,
}: {
  workspaceId: string;
  flowId: string;
  webhookId: string;
}) {
  const deliveries = useAsync(
    () => listDeliveries(workspaceId, flowId, webhookId).then((p) => p.items),
    [workspaceId, flowId, webhookId],
  );

  return (
    <div className="delivery-log">
      <h3>Delivery log</h3>
      {deliveries.status === 'loading' && <p role="status">Loading deliveries…</p>}
      {deliveries.status === 'error' && <p role="alert" className="error">{deliveries.message}</p>}
      {deliveries.status === 'success' && (
        <table>
          <thead>
            <tr>
              <th>Received (UTC)</th>
              <th>Outcome</th>
              <th>Result</th>
            </tr>
          </thead>
          <tbody>
            {deliveries.data.map((d: WebhookDelivery) => (
              <tr key={d.id}>
                <td>{new Date(d.receivedAtUtc).toLocaleString()}</td>
                <td>{d.outcome}</td>
                <td>
                  <span className={`badge ${d.success ? 'status-succeeded' : 'status-failed'}`}>
                    {d.success ? 'OK' : 'Rejected'}
                  </span>
                </td>
              </tr>
            ))}
            {deliveries.data.length === 0 && (
              <tr>
                <td colSpan={3}>No deliveries yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
