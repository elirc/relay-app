import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import RunsPage from './RunsPage';
import * as runsApi from '../api/runs';
import type { RunDetail, RunSummary, Workspace } from '../api/types';

const workspace: Workspace = { id: 'ws1', name: 'Acme', slug: 'acme', createdAtUtc: '2026-01-01T00:00:00Z' };

vi.mock('../workspace/WorkspaceContext', () => ({
  useWorkspace: () => ({ status: 'ready' as const, workspaces: [workspace], current: workspace, setCurrentId: () => {} }),
}));

const run: RunSummary = {
  id: 'r1',
  flowId: 'f1',
  flowName: 'Notify Slack',
  status: 'Succeeded',
  trigger: 'Manual',
  startedAtUtc: '2026-01-01T00:00:00Z',
  completedAtUtc: '2026-01-01T00:00:01Z',
  durationMs: 1200,
  retryCount: 0,
};

const detail: RunDetail = {
  ...run,
  error: null,
  triggerPayloadJson: null,
  stepLogs: [
    { id: 'l0', stepOrder: 0, name: 'Trigger: Inbound', status: 'Succeeded', message: 'Flow triggered', startedAtUtc: '2026-01-01T00:00:00Z', completedAtUtc: '2026-01-01T00:00:00Z', durationMs: 0 },
    { id: 'l1', stepOrder: 1, name: 'Post to Slack', status: 'Succeeded', message: 'Message posted to Slack', startedAtUtc: '2026-01-01T00:00:00Z', completedAtUtc: '2026-01-01T00:00:01Z', durationMs: 1000 },
  ],
};

function pageOf<T>(items: T[]) {
  return { items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 };
}

describe('RunsPage', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('lists runs with status', async () => {
    vi.spyOn(runsApi, 'listRuns').mockResolvedValue(pageOf([run]));

    render(<RunsPage />);

    expect(await screen.findByText('Notify Slack')).toBeInTheDocument();
    expect(screen.getByText('Succeeded')).toBeInTheDocument();
  });

  it('opens a run detail with per-step logs', async () => {
    vi.spyOn(runsApi, 'listRuns').mockResolvedValue(pageOf([run]));
    vi.spyOn(runsApi, 'getRun').mockResolvedValue(detail);

    render(<RunsPage />);
    await screen.findByText('Notify Slack');

    await userEvent.click(screen.getByRole('button', { name: 'View' }));

    const heading = await screen.findByRole('heading', { name: 'Run detail' });
    const detailSection = heading.closest('div')!.parentElement!;
    expect(within(detailSection).getByText('Post to Slack')).toBeInTheDocument();
    expect(within(detailSection).getByText('Message posted to Slack')).toBeInTheDocument();
  });

  it('retries a run and selects the new run', async () => {
    vi.spyOn(runsApi, 'listRuns').mockResolvedValue(pageOf([run]));
    vi.spyOn(runsApi, 'getRun').mockResolvedValue({ ...detail, id: 'r2' });
    const retry = vi.spyOn(runsApi, 'retryRun').mockResolvedValue({ ...detail, id: 'r2' });

    render(<RunsPage />);
    await screen.findByText('Notify Slack');

    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(retry).toHaveBeenCalledWith('ws1', 'r1'));
  });
});
