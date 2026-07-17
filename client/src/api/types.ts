// Shared shapes mirroring the API contracts. Enums are string unions matching
// the server's JsonStringEnumConverter output.

export type AuthKind = 'None' | 'ApiKey' | 'OAuth2' | 'Basic';
export type ConnectionStatus = 'Active' | 'Disabled' | 'Error';
export type RunStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Skipped';
export type RunTrigger = 'Manual' | 'Webhook' | 'Schedule';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface Workspace {
  id: string;
  name: string;
  slug: string;
  createdAtUtc: string;
}

export interface Connector {
  id: string;
  key: string;
  name: string;
  description: string;
  authKind: AuthKind;
  configSchemaJson: string;
  createdAtUtc: string;
}

export interface Connection {
  id: string;
  workspaceId: string;
  connectorId: string;
  connectorKey: string;
  connectorName: string;
  name: string;
  configJson: string;
  hasCredentials: boolean;
  status: ConnectionStatus;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface FlowStep {
  id: string;
  order: number;
  name: string;
  connectionId: string;
  connectionName: string;
  action: string;
  configJson: string;
}

export interface FlowSummary {
  id: string;
  workspaceId: string;
  name: string;
  description?: string | null;
  isEnabled: boolean;
  triggerConnectionId: string;
  triggerConnectionName: string;
  stepCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface FlowDetail {
  id: string;
  workspaceId: string;
  name: string;
  description?: string | null;
  isEnabled: boolean;
  triggerConnectionId: string;
  triggerConnectionName: string;
  steps: FlowStep[];
  createdAtUtc: string;
  updatedAtUtc: string;
}
