import { useWorkspace } from '../workspace/WorkspaceContext';
import { useAsync } from '../hooks/useAsync';
import { getWorkspaceMetrics } from '../api/metrics';
import type { MetricsSummary, TimeBucket } from '../api/metrics';

const DAYS = 14;

function pct(rate: number): string {
  return `${Math.round(rate * 100)}%`;
}

function ms(value: number): string {
  return value < 1000 ? `${value} ms` : `${(value / 1000).toFixed(2)} s`;
}

export default function DashboardPage() {
  const { current, status, message } = useWorkspace();
  if (status === 'loading') return <p role="status">Loading workspace…</p>;
  if (status === 'error') return <p role="alert" className="error">{message}</p>;
  if (!current) return <p>No workspace available.</p>;
  return <DashboardInner workspaceId={current.id} />;
}

function DashboardInner({ workspaceId }: { workspaceId: string }) {
  const metrics = useAsync(() => getWorkspaceMetrics(workspaceId, DAYS), [workspaceId]);

  return (
    <section>
      <h1>Dashboard</h1>
      <p>Run metrics for the last {DAYS} days.</p>

      {metrics.status === 'loading' && <p role="status">Loading metrics…</p>}
      {metrics.status === 'error' && <p role="alert" className="error">{metrics.message}</p>}
      {metrics.status === 'success' && (
        <>
          <StatTiles summary={metrics.data.overall} />

          <h2>Runs over time</h2>
          <RunsOverTime buckets={metrics.data.runsOverTime} />

          <h2>By flow</h2>
          <table>
            <thead>
              <tr>
                <th>Flow</th>
                <th>Runs</th>
                <th>Success</th>
                <th>p50</th>
                <th>p95</th>
              </tr>
            </thead>
            <tbody>
              {metrics.data.perFlow.map((f) => (
                <tr key={f.flowId}>
                  <td>{f.flowName}</td>
                  <td>{f.summary.totalRuns}</td>
                  <td>{pct(f.summary.successRate)}</td>
                  <td>{ms(f.summary.p50DurationMs)}</td>
                  <td>{ms(f.summary.p95DurationMs)}</td>
                </tr>
              ))}
              {metrics.data.perFlow.length === 0 && (
                <tr>
                  <td colSpan={5}>No runs in this window.</td>
                </tr>
              )}
            </tbody>
          </table>
        </>
      )}
    </section>
  );
}

function StatTiles({ summary }: { summary: MetricsSummary }) {
  const tiles: { label: string; value: string }[] = [
    { label: 'Total runs', value: String(summary.totalRuns) },
    { label: 'Success rate', value: pct(summary.successRate) },
    { label: 'Failed', value: String(summary.failed) },
    { label: 'p50 duration', value: ms(summary.p50DurationMs) },
    { label: 'p95 duration', value: ms(summary.p95DurationMs) },
  ];
  return (
    <div className="stat-tiles">
      {tiles.map((t) => (
        <div className="stat-tile" key={t.label}>
          <div className="stat-value">{t.value}</div>
          <div className="stat-label">{t.label}</div>
        </div>
      ))}
    </div>
  );
}

function RunsOverTime({ buckets }: { buckets: TimeBucket[] }) {
  const max = Math.max(1, ...buckets.map((b) => b.total));
  return (
    <div className="runs-chart" role="img" aria-label="Runs per day">
      {buckets.map((b) => (
        <div className="runs-bar-row" key={b.date}>
          <span className="runs-date">{b.date.slice(5)}</span>
          <div className="runs-bar" title={`${b.total} runs (${b.succeeded} ok, ${b.failed} failed)`}>
            <div
              className="runs-bar-ok"
              style={{ width: `${(b.succeeded / max) * 100}%` }}
            />
            <div
              className="runs-bar-fail"
              style={{ width: `${(b.failed / max) * 100}%` }}
            />
          </div>
          <span className="runs-count">{b.total}</span>
        </div>
      ))}
    </div>
  );
}
