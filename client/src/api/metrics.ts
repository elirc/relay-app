import { api } from './client';

export interface MetricsSummary {
  totalRuns: number;
  succeeded: number;
  failed: number;
  successRate: number; // 0..1
  p50DurationMs: number;
  p95DurationMs: number;
}

export interface TimeBucket {
  date: string; // yyyy-MM-dd
  total: number;
  succeeded: number;
  failed: number;
}

export interface FlowMetricsRow {
  flowId: string;
  flowName: string;
  summary: MetricsSummary;
}

export interface WorkspaceMetrics {
  days: number;
  overall: MetricsSummary;
  perFlow: FlowMetricsRow[];
  runsOverTime: TimeBucket[];
}

export interface FlowMetrics {
  flowId: string;
  flowName: string;
  days: number;
  summary: MetricsSummary;
  runsOverTime: TimeBucket[];
}

export function getWorkspaceMetrics(workspaceId: string, days = 7): Promise<WorkspaceMetrics> {
  return api.get<WorkspaceMetrics>(`/api/workspaces/${workspaceId}/metrics?days=${days}`);
}

export function getFlowMetrics(
  workspaceId: string,
  flowId: string,
  days = 7,
): Promise<FlowMetrics> {
  return api.get<FlowMetrics>(`/api/workspaces/${workspaceId}/flows/${flowId}/metrics?days=${days}`);
}
