import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import FlowEditorPage from './FlowEditorPage';
import * as connectionsApi from '../api/connections';
import * as flowsApi from '../api/flows';
import type { Connection, Workspace } from '../api/types';

const workspace: Workspace = { id: 'ws1', name: 'Acme', slug: 'acme', createdAtUtc: '2026-01-01T00:00:00Z' };

vi.mock('../workspace/WorkspaceContext', () => ({
  useWorkspace: () => ({ status: 'ready' as const, workspaces: [workspace], current: workspace, setCurrentId: () => {} }),
}));

const conn = (id: string, name: string): Connection => ({
  id,
  workspaceId: 'ws1',
  connectorId: 'k',
  connectorKey: 'http',
  connectorName: 'HTTP',
  name,
  configJson: '{}',
  hasCredentials: false,
  status: 'Active',
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: '2026-01-01T00:00:00Z',
});

function pageOf<T>(items: T[]) {
  return { items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 };
}

function renderNew() {
  return render(
    <MemoryRouter initialEntries={['/flows/new']}>
      <Routes>
        <Route path="/flows/new" element={<FlowEditorPage />} />
        <Route path="/flows" element={<div>Flows list</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('FlowEditorPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(connectionsApi, 'listConnections').mockResolvedValue(
      pageOf([conn('c1', 'Inbound'), conn('c2', 'Slack')]),
    );
  });

  it('creates a flow with a trigger and step', async () => {
    const create = vi.spyOn(flowsApi, 'createFlow').mockResolvedValue({} as never);

    renderNew();
    await screen.findByLabelText('Trigger connection');

    // Flow-level Name is the first "Name" field; the step has its own "Name".
    await userEvent.type(screen.getAllByLabelText('Name')[0], 'My flow');
    await userEvent.selectOptions(screen.getByLabelText('Trigger connection'), 'c1');

    const step = screen.getByRole('group', { name: /step 1/i });
    await userEvent.type(within(step).getByLabelText('Name'), 'Post');
    await userEvent.selectOptions(within(step).getByLabelText('Connection'), 'c2');
    await userEvent.type(within(step).getByLabelText('Action'), 'send_message');

    await userEvent.click(screen.getByRole('button', { name: /create flow/i }));

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        'ws1',
        expect.objectContaining({
          name: 'My flow',
          triggerConnectionId: 'c1',
          steps: [expect.objectContaining({ name: 'Post', connectionId: 'c2', action: 'send_message' })],
        }),
      ),
    );
  });

  it('adds and removes step cards', async () => {
    renderNew();
    await screen.findByLabelText('Trigger connection');

    expect(screen.getAllByRole('group')).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: 'Add step' }));
    expect(screen.getAllByRole('group')).toHaveLength(2);

    await userEvent.click(screen.getByRole('button', { name: /remove step 2/i }));
    expect(screen.getAllByRole('group')).toHaveLength(1);
  });
});
