import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ConnectionsPage from './ConnectionsPage';
import * as connectionsApi from '../api/connections';
import * as connectorsApi from '../api/connectors';
import type { Connection, Connector, Workspace } from '../api/types';

const workspace: Workspace = {
  id: 'ws1',
  name: 'Acme',
  slug: 'acme',
  createdAtUtc: '2026-01-01T00:00:00Z',
};

vi.mock('../workspace/WorkspaceContext', () => ({
  useWorkspace: () => ({
    status: 'ready' as const,
    workspaces: [workspace],
    current: workspace,
    setCurrentId: () => {},
  }),
}));

const connector: Connector = {
  id: 'con1',
  key: 'slack',
  name: 'Slack',
  description: '',
  authKind: 'OAuth2',
  configSchemaJson: '{}',
  createdAtUtc: '2026-01-01T00:00:00Z',
};

const connection: Connection = {
  id: 'x1',
  workspaceId: 'ws1',
  connectorId: 'con1',
  connectorKey: 'slack',
  connectorName: 'Slack',
  name: 'Acme #alerts',
  configJson: '{}',
  hasCredentials: true,
  status: 'Active',
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: '2026-01-01T00:00:00Z',
};

function page<T>(items: T[]) {
  return { items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 };
}

describe('ConnectionsPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(connectorsApi, 'listConnectors').mockResolvedValue(page([connector]));
  });

  it('lists the workspace connections', async () => {
    vi.spyOn(connectionsApi, 'listConnections').mockResolvedValue(page([connection]));

    render(<ConnectionsPage />);

    expect(await screen.findByText('Acme #alerts')).toBeInTheDocument();
    // Credentials presence rendered without leaking the secret value.
    expect(screen.getByText('set')).toBeInTheDocument();
  });

  it('installs a new connection scoped to the current workspace', async () => {
    vi.spyOn(connectionsApi, 'listConnections').mockResolvedValue(page([connection]));
    const create = vi.spyOn(connectionsApi, 'createConnection').mockResolvedValue(connection);

    render(<ConnectionsPage />);
    await screen.findByText('Acme #alerts');

    await userEvent.selectOptions(screen.getByLabelText('Connector'), 'con1');
    await userEvent.type(screen.getByLabelText('Name'), 'New hook');
    await userEvent.click(screen.getByRole('button', { name: /install connection/i }));

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        'ws1',
        expect.objectContaining({ connectorId: 'con1', name: 'New hook' }),
      ),
    );
  });

  it('renders a schema-driven field and builds config JSON from it', async () => {
    const schemaConnector: Connector = {
      ...connector,
      id: 'con2',
      key: 'slack',
      name: 'Slack',
      configSchemaJson:
        '{"type":"object","properties":{"channel":{"type":"string"}},"required":["channel"]}',
      latestVersion: 1,
    };
    vi.spyOn(connectorsApi, 'listConnectors').mockResolvedValue(page([schemaConnector]));
    vi.spyOn(connectionsApi, 'listConnections').mockResolvedValue(page([]));
    const create = vi.spyOn(connectionsApi, 'createConnection').mockResolvedValue(connection);

    render(<ConnectionsPage />);
    await screen.findByText('No connections yet.');

    await userEvent.selectOptions(screen.getByLabelText('Connector'), 'con2');
    await userEvent.type(screen.getByLabelText('Name'), 'Alerts');
    await userEvent.type(screen.getByLabelText(/channel/i), '#ops');
    await userEvent.click(screen.getByRole('button', { name: /install connection/i }));

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        'ws1',
        expect.objectContaining({ connectorId: 'con2', configJson: '{"channel":"#ops"}' }),
      ),
    );
  });

  it('shows a deprecated badge for a connection on a deprecated version', async () => {
    vi.spyOn(connectionsApi, 'listConnections').mockResolvedValue(
      page([{ ...connection, connectorVersion: 1, isVersionDeprecated: true }]),
    );

    render(<ConnectionsPage />);

    expect(await screen.findByText('deprecated')).toBeInTheDocument();
  });

  it('rotates the secret of a connection that has credentials', async () => {
    vi.spyOn(connectionsApi, 'listConnections').mockResolvedValue(page([connection]));
    const rotate = vi.spyOn(connectionsApi, 'rotateSecret').mockResolvedValue(connection);

    render(<ConnectionsPage />);
    await screen.findByText('Acme #alerts');

    await userEvent.click(screen.getByRole('button', { name: /rotate secret for acme #alerts/i }));

    await waitFor(() => expect(rotate).toHaveBeenCalledWith('ws1', 'x1'));
  });
});
