import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import SchedulesSection from './SchedulesSection';
import * as schedulesApi from '../api/schedules';
import type { Schedule } from '../api/types';

const schedule: Schedule = {
  id: 's1',
  flowId: 'f1',
  cronExpression: '0 9 * * *',
  isEnabled: true,
  nextRunAtUtc: '2026-06-02T09:00:00Z',
  lastRunAtUtc: null,
  createdAtUtc: '2026-06-01T00:00:00Z',
  updatedAtUtc: '2026-06-01T00:00:00Z',
};

const validPreview = { valid: true, nextRuns: ['2026-06-02T09:00:00Z', '2026-06-03T09:00:00Z'] };

describe('SchedulesSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(schedulesApi, 'previewSchedule').mockResolvedValue(validPreview);
  });

  it('lists schedules with their next run', async () => {
    vi.spyOn(schedulesApi, 'listSchedules').mockResolvedValue([schedule]);

    render(<SchedulesSection workspaceId="ws1" flowId="f1" />);

    expect(await screen.findByText('0 9 * * *')).toBeInTheDocument();
    expect(screen.getByText('Enabled')).toBeInTheDocument();
  });

  it('previews a cron expression and creates a schedule', async () => {
    vi.spyOn(schedulesApi, 'listSchedules').mockResolvedValue([]);
    const create = vi.spyOn(schedulesApi, 'createSchedule').mockResolvedValue(schedule);

    render(<SchedulesSection workspaceId="ws1" flowId="f1" />);
    await screen.findByText('No schedules yet.');

    // The live preview turns the default cron into upcoming run times.
    await waitFor(() =>
      expect(schedulesApi.previewSchedule).toHaveBeenCalledWith('ws1', 'f1', '*/15 * * * *'),
    );

    await userEvent.click(screen.getByRole('button', { name: /add schedule/i }));

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith('ws1', 'f1', '*/15 * * * *'),
    );
  });

  it('disables the add button when the cron preview is invalid', async () => {
    vi.spyOn(schedulesApi, 'listSchedules').mockResolvedValue([]);
    vi.spyOn(schedulesApi, 'previewSchedule').mockResolvedValue({ valid: false, nextRuns: [] });

    render(<SchedulesSection workspaceId="ws1" flowId="f1" />);
    await screen.findByText('No schedules yet.');

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /add schedule/i })).toBeDisabled(),
    );
    expect(screen.getByText('Invalid cron expression.')).toBeInTheDocument();
  });
});
