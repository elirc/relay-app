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
  latestVersion?: number;
  isLatestDeprecated?: boolean;
  createdAtUtc: string;
}

export interface ConnectorVersion {
  id: string;
  connectorId: string;
  version: number;
  configSchemaJson: string;
  isDeprecated: boolean;
  createdAtUtc: string;
}

export interface Connection {
  id: string;
  workspaceId: string;
  connectorId: string;
  connectorKey: string;
  connectorName: string;
  connectorVersion?: number | null;
  isVersionDeprecated?: boolean;
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

export interface RunStepLog {
  id: string;
  stepOrder: number;
  name: string;
  status: RunStatus;
  message?: string | null;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  durationMs: number;
}

export interface RunSummary {
  id: string;
  flowId: string;
  flowName: string;
  status: RunStatus;
  trigger: RunTrigger;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  durationMs: number;
  retryCount: number;
}

export interface RunDetail extends RunSummary {
  error?: string | null;
  triggerPayloadJson?: string | null;
  stepLogs: RunStepLog[];
}

export interface Webhook {
  id: string;
  flowId: string;
  token: string;
  url: string;
  isEnabled: boolean;
  createdAtUtc: string;
  lastTriggeredAtUtc?: string | null;
}
