import { api } from './client';
import type { Workspace } from './types';

export function listWorkspaces(): Promise<Workspace[]> {
  return api.get<Workspace[]>('/api/workspaces');
}
