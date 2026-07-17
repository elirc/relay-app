import { useEffect, useState } from 'react';
import { getHealth, type HealthResponse } from '../api/health';
import { ApiError } from '../api/client';

type State =
  | { status: 'loading' }
  | { status: 'ok'; data: HealthResponse }
  | { status: 'error'; message: string };

export default function HealthPage() {
  const [state, setState] = useState<State>({ status: 'loading' });

  useEffect(() => {
    const controller = new AbortController();
    setState({ status: 'loading' });
    getHealth(controller.signal)
      .then((data) => setState({ status: 'ok', data }))
      .catch((err) => {
        if (controller.signal.aborted) return;
        const message = err instanceof ApiError ? err.message : 'Unexpected error';
        setState({ status: 'error', message });
      });
    return () => controller.abort();
  }, []);

  return (
    <section>
      <h1>API health</h1>
      {state.status === 'loading' && <p role="status">Checking API…</p>}
      {state.status === 'error' && (
        <p role="alert" className="error">
          API unreachable: {state.message}
        </p>
      )}
      {state.status === 'ok' && (
        <dl className="health-grid">
          <dt>Status</dt>
          <dd data-testid="health-status">{state.data.status}</dd>
          <dt>Service</dt>
          <dd>{state.data.service}</dd>
          <dt>Timestamp (UTC)</dt>
          <dd>{state.data.timestampUtc}</dd>
        </dl>
      )}
    </section>
  );
}
