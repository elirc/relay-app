import { api } from './client';
import type { FlowDetail, FlowSummary, PagedResult } from './types';

export interface FlowStepInput {
  name: string;
  connectionId: string;
  action: string;
  configJson: string;
  maxAttempts?: number;
  backoffSeconds?: number;
}

export interface FlowInput {
  name: string;
  description?: string | null;
  triggerConnectionId: string;
  steps: FlowStepInput[];
}

const base = (workspaceId: string) => `/api/workspaces/${workspaceId}/flows`;

export function listFlows(
  workspaceId: string,
  page = 1,
  pageSize = 50,
): Promise<PagedResult<FlowSummary>> {
  return api.get<PagedResult<FlowSummary>>(`${base(workspaceId)}?page=${page}&pageSize=${pageSize}`);
}

export function getFlow(workspaceId: string, id: string): Promise<FlowDetail> {
  return api.get<FlowDetail>(`${base(workspaceId)}/${id}`);
}

export function createFlow(workspaceId: string, input: FlowInput): Promise<FlowDetail> {
  return api.post<FlowDetail>(base(workspaceId), input);
}

export function updateFlow(workspaceId: string, id: string, input: FlowInput): Promise<FlowDetail> {
  return api.put<FlowDetail>(`${base(workspaceId)}/${id}`, input);
}

export function enableFlow(workspaceId: string, id: string): Promise<FlowSummary> {
  return api.post<FlowSummary>(`${base(workspaceId)}/${id}/enable`, {});
}

export function disableFlow(workspaceId: string, id: string): Promise<FlowSummary> {
  return api.post<FlowSummary>(`${base(workspaceId)}/${id}/disable`, {});
}

export function deleteFlow(workspaceId: string, id: string): Promise<void> {
  return api.del<void>(`${base(workspaceId)}/${id}`);
}

// ---- Export / import (portable JSON) ----

export interface PortableTrigger {
  connectorKey: string;
  connectionName: string;
}

export interface PortableStep {
  name: string;
  connectorKey: string;
  connectionName: string;
  action: string;
  configJson: string;
  maxAttempts: number;
  backoffSeconds: number;
}

export interface FlowExport {
  externalId: string;
  name: string;
  description?: string | null;
  trigger: PortableTrigger;
  steps: PortableStep[];
}

export interface ImportResult {
  valid: boolean;
  action: string;
  flowId?: string | null;
  issues: string[];
}

export function exportFlow(workspaceId: string, id: string): Promise<FlowExport> {
  return api.get<FlowExport>(`${base(workspaceId)}/${id}/export`);
}

export function importFlow(
  workspaceId: string,
  document: FlowExport,
  dryRun: boolean,
): Promise<ImportResult> {
  return api.post<ImportResult>(`${base(workspaceId)}/import?dryRun=${dryRun}`, document);
}
