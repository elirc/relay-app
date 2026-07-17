import { useState } from 'react';
import { useAsync } from '../hooks/useAsync';
import { createConnector, deleteConnector, listConnectors } from '../api/connectors';
import type { ConnectorInput } from '../api/connectors';
import type { AuthKind } from '../api/types';
import { ApiError } from '../api/client';

const AUTH_KINDS: AuthKind[] = ['None', 'ApiKey', 'OAuth2', 'Basic'];

const emptyForm: ConnectorInput = {
  key: '',
  name: '',
  description: '',
  authKind: 'None',
  configSchemaJson: '{}',
};

export default function ConnectorsPage() {
  const connectors = useAsync(() => listConnectors(1, 100).then((p) => p.items), []);
  const [form, setForm] = useState<ConnectorInput>(emptyForm);
  const [formError, setFormError] = useState<string>();
  const [busy, setBusy] = useState(false);

  async function onCreate(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setFormError(undefined);
    try {
      await createConnector(form);
      setForm(emptyForm);
      connectors.reload();
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Failed to create connector');
    } finally {
      setBusy(false);
    }
  }

  async function onDelete(id: string) {
    try {
      await deleteConnector(id);
      connectors.reload();
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Failed to delete connector');
    }
  }

  return (
    <section>
      <h1>Connectors</h1>
      <p>The catalog of integration types available to install as connections.</p>

      {connectors.status === 'loading' && <p role="status">Loading catalog…</p>}
      {connectors.status === 'error' && (
        <p role="alert" className="error">
          {connectors.message}
        </p>
      )}
      {connectors.status === 'success' && (
        <table>
          <thead>
            <tr>
              <th>Key</th>
              <th>Name</th>
              <th>Auth</th>
              <th>Description</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {connectors.data.map((c) => (
              <tr key={c.id}>
                <td>
                  <code>{c.key}</code>
                </td>
                <td>{c.name}</td>
                <td>{c.authKind}</td>
                <td>{c.description}</td>
                <td>
                  <button type="button" onClick={() => onDelete(c.id)} aria-label={`Delete ${c.key}`}>
                    Delete
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Add a connector</h2>
      <form onSubmit={onCreate} className="stack">
        <label>
          Key
          <input
            value={form.key}
            onChange={(e) => setForm({ ...form, key: e.target.value })}
            placeholder="e.g. stripe"
            required
          />
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
          Auth kind
          <select
            value={form.authKind}
            onChange={(e) => setForm({ ...form, authKind: e.target.value as AuthKind })}
          >
            {AUTH_KINDS.map((k) => (
              <option key={k} value={k}>
                {k}
              </option>
            ))}
          </select>
        </label>
        <label>
          Description
          <input
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
          />
        </label>
        {formError && (
          <p role="alert" className="error">
            {formError}
          </p>
        )}
        <div>
          <button type="submit" disabled={busy}>
            {busy ? 'Saving…' : 'Create connector'}
          </button>
        </div>
      </form>
    </section>
  );
}
