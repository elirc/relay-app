import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useWorkspace } from '../workspace/WorkspaceContext';
import { useAsync } from '../hooks/useAsync';
import { instantiateTemplate, listTemplates } from '../api/templates';
import { ApiError } from '../api/client';

export default function TemplatesPage() {
  const { current, status, message } = useWorkspace();
  if (status === 'loading') return <p role="status">Loading workspace…</p>;
  if (status === 'error') return <p role="alert" className="error">{message}</p>;
  if (!current) return <p>No workspace available.</p>;
  return <TemplatesInner workspaceId={current.id} />;
}

function TemplatesInner({ workspaceId }: { workspaceId: string }) {
  const templates = useAsync(() => listTemplates(), []);
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState<string>();
  const navigate = useNavigate();

  async function use(templateId: string) {
    setError(undefined);
    setBusy(templateId);
    try {
      const flow = await instantiateTemplate(workspaceId, templateId);
      navigate(`/flows/${flow.id}`);
    } catch (err) {
      setError(
        err instanceof ApiError
          ? err.problem?.detail ?? err.message
          : 'Failed to instantiate template',
      );
    } finally {
      setBusy(undefined);
    }
  }

  return (
    <section>
      <h1>Templates</h1>
      <p>Start from a predefined flow. Instantiating maps its connectors to your connections.</p>
      {error && <p role="alert" className="error">{error}</p>}

      {templates.status === 'loading' && <p role="status">Loading templates…</p>}
      {templates.status === 'error' && <p role="alert" className="error">{templates.message}</p>}
      {templates.status === 'success' && (
        <div className="template-gallery">
          {templates.data.map((t) => (
            <article className="template-card" key={t.id}>
              <span className="badge">{t.category}</span>
              <h2>{t.name}</h2>
              <p>{t.description}</p>
              <p className="template-steps">
                Trigger: <code>{t.triggerConnectorKey}</code> · {t.steps.length} step
                {t.steps.length === 1 ? '' : 's'}
              </p>
              <button type="button" onClick={() => use(t.id)} disabled={busy === t.id}>
                {busy === t.id ? 'Creating…' : 'Use template'}
              </button>
            </article>
          ))}
          {templates.data.length === 0 && <p>No templates available.</p>}
        </div>
      )}
    </section>
  );
}
