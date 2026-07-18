import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import DashboardPage from './DashboardPage';
import * as metricsApi from '../api/metrics';
import type { WorkspaceMetrics } from '../api/metrics';
import type { Workspace } from '../api/types';

const workspace: Workspace = { id: 'ws1', name: 'Acme', slug: 'acme', createdAtUtc: '2026-01-01T00:00:00Z' };

vi.mock('../workspace/WorkspaceContext', () => ({
  useWorkspace: () => ({ status: 'ready' as const, workspaces: [workspace], current: workspace, setCurrentId: () => {} }),
}));

const metrics: WorkspaceMetrics = {
  days: 14,
  overall: { totalRuns: 10, succeeded: 8, failed: 2, successRate: 0.8, p50DurationMs: 120, p95DurationMs: 900 },
  perFlow: [
    {
      flowId: 'f1',
      flowName: 'Notify Slack',
      summary: { totalRuns: 10, succeeded: 8, failed: 2, successRate: 0.8, p50DurationMs: 120, p95DurationMs: 900 },
    },
  ],
  runsOverTime: [
    { date: '2026-06-01', total: 4, succeeded: 3, failed: 1 },
    { date: '2026-06-02', total: 6, succeeded: 5, failed: 1 },
  ],
};

describe('DashboardPage', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('renders summary tiles, per-flow rows, and a runs chart', async () => {
    vi.spyOn(metricsApi, 'getWorkspaceMetrics').mockResolvedValue(metrics);

    render(<DashboardPage />);

    // Per-flow row.
    expect(await screen.findByText('Notify Slack')).toBeInTheDocument();
    // Success rate appears (overall tile + per-flow cell).
    expect(screen.getAllByText('80%').length).toBeGreaterThan(0);
    // Summary tile labels.
    expect(screen.getByText('Success rate')).toBeInTheDocument();
    // Runs-over-time chart is present.
    expect(screen.getByRole('img', { name: /runs per day/i })).toBeInTheDocument();
  });

  it('shows an empty state when there are no runs', async () => {
    vi.spyOn(metricsApi, 'getWorkspaceMetrics').mockResolvedValue({
      ...metrics,
      overall: { totalRuns: 0, succeeded: 0, failed: 0, successRate: 0, p50DurationMs: 0, p95DurationMs: 0 },
      perFlow: [],
    });

    render(<DashboardPage />);

    expect(await screen.findByText('No runs in this window.')).toBeInTheDocument();
  });
});
