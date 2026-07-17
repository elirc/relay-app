import { api } from './client';
import type { PagedResult, RunDetail, RunSummary } from './types';

const wsBase = (workspaceId: string) => `/api/workspaces/${workspaceId}`;

export function runFlow(
  workspaceId: string,
  flowId: string,
  payloadJson?: string,
): Promise<RunDetail> {
  return api.post<RunDetail>(`${wsBase(workspaceId)}/flows/${flowId}/run`, {
    payloadJson: payloadJson ?? null,
  });
}

export function listRuns(
  workspaceId: string,
  page = 1,
  pageSize = 50,
): Promise<PagedResult<RunSummary>> {
  return api.get<PagedResult<RunSummary>>(`${wsBase(workspaceId)}/runs?page=${page}&pageSize=${pageSize}`);
}

export function getRun(workspaceId: string, runId: string): Promise<RunDetail> {
  return api.get<RunDetail>(`${wsBase(workspaceId)}/runs/${runId}`);
}

export function retryRun(workspaceId: string, runId: string): Promise<RunDetail> {
  return api.post<RunDetail>(`${wsBase(workspaceId)}/runs/${runId}/retry`, {});
}
