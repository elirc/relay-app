import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ConnectorsPage from './ConnectorsPage';
import * as connectorsApi from '../api/connectors';
import type { Connector } from '../api/types';

const sample: Connector = {
  id: 'c1',
  key: 'slack',
  name: 'Slack',
  description: 'Post messages',
  authKind: 'OAuth2',
  configSchemaJson: '{}',
  createdAtUtc: '2026-01-01T00:00:00Z',
};

function page<T>(items: T[]) {
  return { items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 };
}

describe('ConnectorsPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('renders the connector catalog', async () => {
    vi.spyOn(connectorsApi, 'listConnectors').mockResolvedValue(page([sample]));

    render(<ConnectorsPage />);

    expect(await screen.findByText('Slack')).toBeInTheDocument();
    // Target the table cell (the form's auth-kind <select> also lists "OAuth2").
    expect(screen.getByRole('cell', { name: 'OAuth2' })).toBeInTheDocument();
  });

  it('creates a connector and reloads the list', async () => {
    const list = vi.spyOn(connectorsApi, 'listConnectors').mockResolvedValue(page([sample]));
    const create = vi.spyOn(connectorsApi, 'createConnector').mockResolvedValue({
      ...sample,
      id: 'c2',
      key: 'stripe',
      name: 'Stripe',
    });

    render(<ConnectorsPage />);
    await screen.findByText('Slack');

    await userEvent.type(screen.getByLabelText('Key'), 'stripe');
    await userEvent.type(screen.getByLabelText('Name'), 'Stripe');
    await userEvent.click(screen.getByRole('button', { name: /create connector/i }));

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        expect.objectContaining({ key: 'stripe', name: 'Stripe' }),
      ),
    );
    // Initial load + reload after create.
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('surfaces a delete conflict error', async () => {
    vi.spyOn(connectorsApi, 'listConnectors').mockResolvedValue(page([sample]));
    const { ApiError } = await import('../api/client');
    vi.spyOn(connectorsApi, 'deleteConnector').mockRejectedValue(
      new ApiError(409, 'Connector in use'),
    );

    render(<ConnectorsPage />);
    await screen.findByText('Slack');

    await userEvent.click(screen.getByRole('button', { name: /delete slack/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Connector in use');
  });
});
