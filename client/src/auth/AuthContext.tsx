import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { login as apiLogin } from '../api/auth';
import type { AuthUser } from '../api/auth';
import { setAuthToken, setUnauthorizedHandler } from '../api/client';

const TOKEN_KEY = 'relay.token';
const USER_KEY = 'relay.user';

interface AuthState {
  token: string | null;
  user: AuthUser | null;
}

interface AuthContextValue {
  token: string | null;
  user: AuthUser | null;
  isAuthenticated: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

function readStored(): AuthState {
  try {
    const token = localStorage.getItem(TOKEN_KEY);
    const userRaw = localStorage.getItem(USER_KEY);
    return { token, user: userRaw ? (JSON.parse(userRaw) as AuthUser) : null };
  } catch {
    return { token: null, user: null };
  }
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>(readStored);

  // Sync the token into the API client during render (top-down, before any
  // child provider's effects fire) so the first data fetch carries it.
  setAuthToken(state.token);

  const logout = useCallback(() => {
    setAuthToken(null);
    try {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(USER_KEY);
    } catch {
      /* ignore storage errors */
    }
    setState({ token: null, user: null });
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const res = await apiLogin(email, password);
    setAuthToken(res.token);
    try {
      localStorage.setItem(TOKEN_KEY, res.token);
      localStorage.setItem(USER_KEY, JSON.stringify(res.user));
    } catch {
      /* ignore storage errors */
    }
    setState({ token: res.token, user: res.user });
  }, []);

  // A 401 from any call means the session is gone — drop it.
  useEffect(() => {
    setUnauthorizedHandler(logout);
    return () => setUnauthorizedHandler(null);
  }, [logout]);

  const value = useMemo<AuthContextValue>(
    () => ({
      token: state.token,
      user: state.user,
      isAuthenticated: Boolean(state.token),
      login,
      logout,
    }),
    [state, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider');
  return ctx;
}
