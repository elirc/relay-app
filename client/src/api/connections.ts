import { api } from './client';
import type { Connection, ConnectionStatus, PagedResult } from './types';

export interface CreateConnectionInput {
  connectorId: string;
  name: string;
  configJson: string;
  credentialsJson?: string;
  connectorVersion?: number;
}

export interface UpdateConnectionInput {
  name: string;
  configJson: string;
  credentialsJson?: string;
  status: ConnectionStatus;
}

const base = (workspaceId: string) => `/api/workspaces/${workspaceId}/connections`;

export function listConnections(
  workspaceId: string,
  page = 1,
  pageSize = 50,
): Promise<PagedResult<Connection>> {
  return api.get<PagedResult<Connection>>(`${base(workspaceId)}?page=${page}&pageSize=${pageSize}`);
}

export function createConnection(
  workspaceId: string,
  input: CreateConnectionInput,
): Promise<Connection> {
  return api.post<Connection>(base(workspaceId), input);
}

export function updateConnection(
  workspaceId: string,
  id: string,
  input: UpdateConnectionInput,
): Promise<Connection> {
  return api.put<Connection>(`${base(workspaceId)}/${id}`, input);
}

export function deleteConnection(workspaceId: string, id: string): Promise<void> {
  return api.del<void>(`${base(workspaceId)}/${id}`);
}

export function rotateSecret(workspaceId: string, id: string): Promise<Connection> {
  return api.post<Connection>(`${base(workspaceId)}/${id}/rotate-secret`, {});
}
