import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import TemplatesPage from './TemplatesPage';
import * as templatesApi from '../api/templates';
import type { FlowTemplate } from '../api/templates';
import type { Workspace } from '../api/types';

const workspace: Workspace = { id: 'ws1', name: 'Acme', slug: 'acme', createdAtUtc: '2026-01-01T00:00:00Z' };

vi.mock('../workspace/WorkspaceContext', () => ({
  useWorkspace: () => ({ status: 'ready' as const, workspaces: [workspace], current: workspace, setCurrentId: () => {} }),
}));

const template: FlowTemplate = {
  id: 't1',
  name: 'Slack alert on webhook',
  description: 'Post to Slack when a webhook arrives.',
  category: 'Notifications',
  triggerConnectorKey: 'http',
  steps: [
    { name: 'Post to Slack', connectorKey: 'slack', action: 'send_message', configJson: '{}', maxAttempts: 3, backoffSeconds: 0 },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/templates']}>
      <Routes>
        <Route path="/templates" element={<TemplatesPage />} />
        <Route path="/flows/:id" element={<div>Flow editor</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('TemplatesPage', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('lists the template gallery', async () => {
    vi.spyOn(templatesApi, 'listTemplates').mockResolvedValue([template]);

    renderPage();

    expect(await screen.findByText('Slack alert on webhook')).toBeInTheDocument();
    expect(screen.getByText('Notifications')).toBeInTheDocument();
  });

  it('instantiates a template and navigates to the new draft', async () => {
    vi.spyOn(templatesApi, 'listTemplates').mockResolvedValue([template]);
    const instantiate = vi
      .spyOn(templatesApi, 'instantiateTemplate')
      .mockResolvedValue({ id: 'f9' } as never);

    renderPage();
    await screen.findByText('Slack alert on webhook');

    await userEvent.click(screen.getByRole('button', { name: /use template/i }));

    await waitFor(() => expect(instantiate).toHaveBeenCalledWith('ws1', 't1'));
    expect(await screen.findByText('Flow editor')).toBeInTheDocument();
  });
});
