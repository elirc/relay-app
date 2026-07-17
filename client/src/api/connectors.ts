import { api } from './client';
import type { Connector, PagedResult, AuthKind } from './types';

export interface ConnectorInput {
  key: string;
  name: string;
  description: string;
  authKind: AuthKind;
  configSchemaJson: string;
}

export function listConnectors(page = 1, pageSize = 50): Promise<PagedResult<Connector>> {
  return api.get<PagedResult<Connector>>(`/api/connectors?page=${page}&pageSize=${pageSize}`);
}

export function getConnector(id: string): Promise<Connector> {
  return api.get<Connector>(`/api/connectors/${id}`);
}

export function createConnector(input: ConnectorInput): Promise<Connector> {
  return api.post<Connector>('/api/connectors', input);
}

export function updateConnector(id: string, input: Omit<ConnectorInput, 'key'>): Promise<Connector> {
  return api.put<Connector>(`/api/connectors/${id}`, input);
}

export function deleteConnector(id: string): Promise<void> {
  return api.del<void>(`/api/connectors/${id}`);
}
