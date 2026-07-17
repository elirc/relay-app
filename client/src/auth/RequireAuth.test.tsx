import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import RequireAuth from './RequireAuth';
import { AuthProvider } from './AuthContext';
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

function renderGuard(seedToken = false) {
  if (seedToken) {
    localStorage.setItem('relay.token', 'tok');
    localStorage.setItem('relay.user', JSON.stringify(user));
  }
  return render(
    <MemoryRouter initialEntries={['/secret']}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<div>Login screen</div>} />
          <Route element={<RequireAuth />}>
            <Route path="/secret" element={<div>Secret area</div>} />
          </Route>
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('RequireAuth', () => {
  beforeEach(() => localStorage.clear());

  it('redirects to login when unauthenticated', () => {
    renderGuard(false);
    expect(screen.getByText('Login screen')).toBeInTheDocument();
    expect(screen.queryByText('Secret area')).not.toBeInTheDocument();
  });

  it('renders the protected route when a stored token is present', () => {
    renderGuard(true);
    expect(screen.getByText('Secret area')).toBeInTheDocument();
  });
});
