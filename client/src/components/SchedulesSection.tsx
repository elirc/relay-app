import { useEffect, useState } from 'react';
import { useAsync } from '../hooks/useAsync';
import {
  createSchedule,
  deleteSchedule,
  disableSchedule,
  enableSchedule,
  listSchedules,
  previewSchedule,
} from '../api/schedules';
import type { SchedulePreview } from '../api/schedules';
import { ApiError } from '../api/client';

function fmt(value?: string | null): string {
  if (!value) return '—';
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? value : d.toLocaleString();
}

/** Cron schedule editor for a flow: list, add (with a live next-runs preview), toggle, delete. */
export default function SchedulesSection({
  workspaceId,
  flowId,
}: {
  workspaceId: string;
  flowId: string;
}) {
  const schedules = useAsync(() => listSchedules(workspaceId, flowId), [workspaceId, flowId]);
  const [cron, setCron] = useState('*/15 * * * *');
  const [preview, setPreview] = useState<SchedulePreview>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  // Live-preview the cron expression the user is editing.
  useEffect(() => {
    let active = true;
    if (!cron.trim()) {
      setPreview(undefined);
      return;
    }
    previewSchedule(workspaceId, flowId, cron)
      .then((p) => {
        if (active) setPreview(p);
      })
      .catch(() => {
        if (active) setPreview({ valid: false, nextRuns: [] });
      });
    return () => {
      active = false;
    };
  }, [cron, workspaceId, flowId]);

  async function add() {
    setBusy(true);
    setError(undefined);
    try {
      await createSchedule(workspaceId, flowId, cron);
      schedules.reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to create schedule');
    } finally {
      setBusy(false);
    }
  }

  async function toggle(id: string, isEnabled: boolean) {
    setError(undefined);
    try {
      await (isEnabled ? disableSchedule : enableSchedule)(workspaceId, flowId, id);
      schedules.reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to toggle schedule');
    }
  }

  async function remove(id: string) {
    setError(undefined);
    try {
      await deleteSchedule(workspaceId, flowId, id);
      schedules.reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to delete schedule');
    }
  }

  return (
    <div className="schedules">
      <h2>Schedules</h2>
      <p>Cron-style triggers. Times are UTC; the flow must be enabled to run.</p>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {schedules.status === 'loading' && <p role="status">Loading schedules…</p>}
      {schedules.status === 'error' && (
        <p role="alert" className="error">
          {schedules.message}
        </p>
      )}
      {schedules.status === 'success' && (
        <table>
          <thead>
            <tr>
              <th>Cron</th>
              <th>State</th>
              <th>Next run (UTC)</th>
              <th>Last run</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {schedules.data.map((s) => (
              <tr key={s.id}>
                <td>
                  <code>{s.cronExpression}</code>
                </td>
                <td>
                  <span className="badge">{s.isEnabled ? 'Enabled' : 'Disabled'}</span>
                </td>
                <td>{fmt(s.nextRunAtUtc)}</td>
                <td>{fmt(s.lastRunAtUtc)}</td>
                <td className="actions">
                  <button type="button" onClick={() => toggle(s.id, s.isEnabled)}>
                    {s.isEnabled ? 'Disable' : 'Enable'}
                  </button>
                  <button
                    type="button"
                    onClick={() => remove(s.id)}
                    aria-label={`Delete schedule ${s.cronExpression}`}
                  >
                    Delete
                  </button>
                </td>
              </tr>
            ))}
            {schedules.data.length === 0 && (
              <tr>
                <td colSpan={5}>No schedules yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      )}

      <div className="stack">
        <label>
          Cron expression
          <input value={cron} onChange={(e) => setCron(e.target.value)} placeholder="*/15 * * * *" />
        </label>
        <div aria-live="polite">
          {preview?.valid ? (
            <ul className="next-runs">
              {preview.nextRuns.map((r) => (
                <li key={r}>{fmt(r)}</li>
              ))}
            </ul>
          ) : (
            <p className="error">Invalid cron expression.</p>
          )}
        </div>
        <div>
          <button type="button" onClick={add} disabled={busy || !preview?.valid}>
            {busy ? 'Adding…' : 'Add schedule'}
          </button>
        </div>
      </div>
    </div>
  );
}
