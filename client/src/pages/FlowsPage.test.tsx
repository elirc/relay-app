import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import FlowsPage from './FlowsPage';
import * as flowsApi from '../api/flows';
import type { FlowSummary, Workspace } from '../api/types';

const workspace: Workspace = { id: 'ws1', name: 'Acme', slug: 'acme', createdAtUtc: '2026-01-01T00:00:00Z' };

vi.mock('../workspace/WorkspaceContext', () => ({
  useWorkspace: () => ({ status: 'ready' as const, workspaces: [workspace], current: workspace, setCurrentId: () => {} }),
}));

const flow: FlowSummary = {
  id: 'f1',
  workspaceId: 'ws1',
  name: 'Notify Slack',
  description: null,
  isEnabled: false,
  triggerConnectionId: 'c1',
  triggerConnectionName: 'Inbound',
  stepCount: 2,
  concurrencyToken: 'tok-1',
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: '2026-01-01T00:00:00Z',
};

function page<T>(items: T[]) {
  return { items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <FlowsPage />
    </MemoryRouter>,
  );
}

describe('FlowsPage', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('lists flows with their state', async () => {
    vi.spyOn(flowsApi, 'listFlows').mockResolvedValue(page([flow]));

    renderPage();

    expect(await screen.findByRole('link', { name: 'Notify Slack' })).toBeInTheDocument();
    expect(screen.getByText('Disabled')).toBeInTheDocument();
  });

  it('enables a disabled flow and reloads', async () => {
    const list = vi.spyOn(flowsApi, 'listFlows').mockResolvedValue(page([flow]));
    const enable = vi.spyOn(flowsApi, 'enableFlow').mockResolvedValue({ ...flow, isEnabled: true });

    renderPage();
    await screen.findByText('Notify Slack');

    await userEvent.click(screen.getByRole('button', { name: 'Enable' }));

    await waitFor(() => expect(enable).toHaveBeenCalledWith('ws1', 'f1'));
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('deletes a flow', async () => {
    vi.spyOn(flowsApi, 'listFlows').mockResolvedValue(page([flow]));
    const del = vi.spyOn(flowsApi, 'deleteFlow').mockResolvedValue(undefined);

    renderPage();
    await screen.findByText('Notify Slack');

    await userEvent.click(screen.getByRole('button', { name: /delete notify slack/i }));

    await waitFor(() => expect(del).toHaveBeenCalledWith('ws1', 'f1'));
  });

  it('exports a flow as JSON', async () => {
    vi.spyOn(flowsApi, 'listFlows').mockResolvedValue(page([flow]));
    vi.spyOn(flowsApi, 'exportFlow').mockResolvedValue({
      externalId: 'ext-1',
      name: 'Notify Slack',
      description: null,
      trigger: { connectorKey: 'http', connectionName: 'Inbound' },
      steps: [],
    });

    renderPage();
    await screen.findByText('Notify Slack');

    await userEvent.click(screen.getByRole('button', { name: /export notify slack/i }));

    const textarea = (await screen.findByLabelText('Exported flow JSON')) as HTMLTextAreaElement;
    expect(textarea.value).toContain('ext-1');
  });

  it('validates an imported flow document (dry run)', async () => {
    vi.spyOn(flowsApi, 'listFlows').mockResolvedValue(page([flow]));
    const importSpy = vi
      .spyOn(flowsApi, 'importFlow')
      .mockResolvedValue({ valid: true, action: 'create', flowId: null, issues: [] });

    renderPage();
    await screen.findByText('Notify Slack');

    const json =
      '{"externalId":"x","name":"n","trigger":{"connectorKey":"http","connectionName":"c"},"steps":[]}';
    fireEvent.change(screen.getByLabelText('Import flow JSON'), { target: { value: json } });
    await userEvent.click(screen.getByRole('button', { name: 'Validate' }));

    await waitFor(() => expect(importSpy).toHaveBeenCalledWith('ws1', expect.anything(), true));
    expect(await screen.findByText(/will create/i)).toBeInTheDocument();
  });
});
