import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import HealthPage from './HealthPage';
import * as healthApi from '../api/health';
import { ApiError } from '../api/client';

describe('HealthPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('renders the API status once the health call resolves', async () => {
    vi.spyOn(healthApi, 'getHealth').mockResolvedValue({
      status: 'ok',
      service: 'relay-api',
      timestampUtc: '2026-01-01T00:00:00Z',
    });

    render(<HealthPage />);

    expect(await screen.findByTestId('health-status')).toHaveTextContent('ok');
    expect(screen.getByText('relay-api')).toBeInTheDocument();
  });

  it('shows an error message when the API is unreachable', async () => {
    vi.spyOn(healthApi, 'getHealth').mockRejectedValue(new ApiError(0, 'Network error'));

    render(<HealthPage />);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/unreachable/i);
    });
  });
});
