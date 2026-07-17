import { api } from './client';

export type WorkspaceRole = 'Member' | 'Admin';

export interface AuthUser {
  userId: string;
  email: string;
  displayName: string;
  role: WorkspaceRole;
  workspaceId: string;
  workspaceName: string;
  workspaceSlug: string;
}

export interface LoginResponse {
  token: string;
  expiresAtUtc: string;
  user: AuthUser;
}

export function login(email: string, password: string): Promise<LoginResponse> {
  return api.post<LoginResponse>('/api/auth/login', { email, password });
}

export function getMe(): Promise<AuthUser> {
  return api.get<AuthUser>('/api/auth/me');
}
