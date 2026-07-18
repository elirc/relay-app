import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import DeadLetterPage from './DeadLetterPage';
import * as runsApi from '../api/runs';
import { ApiError } from '../api/client';
import type { RunSummary, Workspace } from '../api/types';

const workspace: Workspace = { id: 'ws1', name: 'Acme', slug: 'acme', createdAtUtc: '2026-01-01T00:00:00Z' };

vi.mock('../workspace/WorkspaceContext', () => ({
  useWorkspace: () => ({ status: 'ready' as const, workspaces: [workspace], current: workspace, setCurrentId: () => {} }),
}));

const failed: RunSummary = {
  id: 'r1',
  flowId: 'f1',
  flowName: 'Notify Slack',
  status: 'Failed',
  trigger: 'Manual',
  startedAtUtc: '2026-06-01T00:00:00Z',
  completedAtUtc: '2026-06-01T00:00:01Z',
  durationMs: 1000,
  retryCount: 2,
};

function page<T>(items: T[]) {
  return { items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 };
}

describe('DeadLetterPage', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('lists failed runs', async () => {
    vi.spyOn(runsApi, 'listDeadLetter').mockResolvedValue(page([failed]));

    render(<DeadLetterPage />);

    expect(await screen.findByText('Notify Slack')).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
  });

  it('replays a failed run from the chosen step', async () => {
    vi.spyOn(runsApi, 'listDeadLetter').mockResolvedValue(page([failed]));
    const replay = vi
      .spyOn(runsApi, 'replayRun')
      .mockResolvedValue({ ...failed, id: 'r2', status: 'Succeeded', stepLogs: [] } as never);

    render(<DeadLetterPage />);
    await screen.findByText('Notify Slack');

    await userEvent.clear(screen.getByLabelText(/replay notify slack from step/i));
    await userEvent.type(screen.getByLabelText(/replay notify slack from step/i), '1');
    await userEvent.click(screen.getByRole('button', { name: /^replay notify slack$/i }));

    await waitFor(() => expect(replay).toHaveBeenCalledWith('ws1', 'r1', 1));
  });

  it('shows a confirmation notice after a successful replay', async () => {
    vi.spyOn(runsApi, 'listDeadLetter').mockResolvedValue(page([failed]));
    vi.spyOn(runsApi, 'replayRun').mockResolvedValue(
      { ...failed, id: 'r2', status: 'Succeeded', stepLogs: [] } as never,
    );

    render(<DeadLetterPage />);
    await screen.findByText('Notify Slack');
    await userEvent.click(screen.getByRole('button', { name: /^replay notify slack$/i }));

    expect(await screen.findByText(/replayed as run r2 \(succeeded\)/i)).toBeInTheDocument();
  });

  it('surfaces an error when the replay request fails', async () => {
    vi.spyOn(runsApi, 'listDeadLetter').mockResolvedValue(page([failed]));
    vi.spyOn(runsApi, 'replayRun').mockRejectedValue(new ApiError(404, 'Run not found'));

    render(<DeadLetterPage />);
    await screen.findByText('Notify Slack');
    await userEvent.click(screen.getByRole('button', { name: /^replay notify slack$/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Run not found');
  });
});
