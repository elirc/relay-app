import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import WebhooksSection from './WebhooksSection';
import * as webhooksApi from '../api/webhooks';
import type { Webhook } from '../api/types';

const unsigned: Webhook = {
  id: 'w1',
  flowId: 'f1',
  token: 'tok-1',
  url: 'http://localhost:5080/api/hooks/tok-1',
  isEnabled: true,
  requireSignature: false,
  hasSigningSecret: false,
  createdAtUtc: '2026-01-01T00:00:00Z',
  lastTriggeredAtUtc: null,
};

describe('WebhooksSection', () => {
  beforeEach(() => vi.restoreAllMocks());

  it('lists webhooks with their signing state', async () => {
    vi.spyOn(webhooksApi, 'listWebhooks').mockResolvedValue([unsigned]);

    render(<WebhooksSection workspaceId="ws1" flowId="f1" />);

    expect(await screen.findByText(unsigned.url)).toBeInTheDocument();
    expect(screen.getByText('Off')).toBeInTheDocument();
  });

  it('enables signing and shows the secret once', async () => {
    vi.spyOn(webhooksApi, 'listWebhooks').mockResolvedValue([unsigned]);
    const generate = vi.spyOn(webhooksApi, 'generateSigningSecret').mockResolvedValue({
      signingSecret: 'super-secret-hex',
      timestampHeader: 'X-Relay-Timestamp',
      signatureHeader: 'X-Relay-Signature',
    });

    render(<WebhooksSection workspaceId="ws1" flowId="f1" />);
    await screen.findByText(unsigned.url);

    await userEvent.click(screen.getByRole('button', { name: /enable signing/i }));

    await waitFor(() => expect(generate).toHaveBeenCalledWith('ws1', 'f1', 'w1'));
    expect(await screen.findByText('super-secret-hex')).toBeInTheDocument();
  });

  it('loads the delivery log on demand', async () => {
    vi.spyOn(webhooksApi, 'listWebhooks').mockResolvedValue([
      { ...unsigned, requireSignature: true, hasSigningSecret: true },
    ]);
    const deliveries = vi.spyOn(webhooksApi, 'listDeliveries').mockResolvedValue({
      items: [
        {
          id: 'd1',
          receivedAtUtc: '2026-06-01T00:00:00Z',
          success: false,
          outcome: 'InvalidSignature',
          runId: null,
          detail: 'Signature did not match.',
        },
      ],
      page: 1,
      pageSize: 50,
      totalCount: 1,
      totalPages: 1,
    });

    render(<WebhooksSection workspaceId="ws1" flowId="f1" />);
    await screen.findByText(unsigned.url);

    await userEvent.click(screen.getByRole('button', { name: 'Deliveries' }));

    await waitFor(() => expect(deliveries).toHaveBeenCalledWith('ws1', 'f1', 'w1'));
    expect(await screen.findByText('InvalidSignature')).toBeInTheDocument();
  });
});
