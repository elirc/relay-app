import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import LoginPage from './LoginPage';
import { AuthProvider } from '../auth/AuthContext';
import * as authApi from '../api/auth';
import { ApiError } from '../api/client';
import type { AuthUser } from '../api/auth';

const user: AuthUser = {
  userId: 'u1',
  email: 'owner@acme.test',
  displayName: 'Ada Owner',
  role: 'Admin',
  workspaceId: 'ws1',
  workspaceName: 'Acme',
  workspaceSlug: 'acme',
};

function renderLogin() {
  return render(
    <MemoryRouter initialEntries={['/login']}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<div>Home dashboard</div>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('signs in with valid credentials and redirects home', async () => {
    const login = vi
      .spyOn(authApi, 'login')
      .mockResolvedValue({ token: 'tok', expiresAtUtc: '2026-01-01T00:00:00Z', user });

    renderLogin();

    await userEvent.type(screen.getByLabelText('Email'), 'owner@acme.test');
    await userEvent.type(screen.getByLabelText('Password'), 'password123');
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(login).toHaveBeenCalledWith('owner@acme.test', 'password123'));
    expect(await screen.findByText('Home dashboard')).toBeInTheDocument();
  });

  it('shows an error when credentials are rejected', async () => {
    vi.spyOn(authApi, 'login').mockRejectedValue(new ApiError(401, 'Invalid credentials'));

    renderLogin();

    await userEvent.type(screen.getByLabelText('Email'), 'owner@acme.test');
    await userEvent.type(screen.getByLabelText('Password'), 'wrong');
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/incorrect/i);
  });
});
