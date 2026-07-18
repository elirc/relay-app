import { api } from './client';
import type { Schedule } from './types';

export interface SchedulePreview {
  valid: boolean;
  error?: string | null;
  nextRuns: string[];
}

const base = (workspaceId: string, flowId: string) =>
  `/api/workspaces/${workspaceId}/flows/${flowId}/schedules`;

export function listSchedules(workspaceId: string, flowId: string): Promise<Schedule[]> {
  return api.get<Schedule[]>(base(workspaceId, flowId));
}

export function createSchedule(
  workspaceId: string,
  flowId: string,
  cronExpression: string,
): Promise<Schedule> {
  return api.post<Schedule>(base(workspaceId, flowId), { cronExpression });
}

export function updateSchedule(
  workspaceId: string,
  flowId: string,
  id: string,
  cronExpression: string,
): Promise<Schedule> {
  return api.put<Schedule>(`${base(workspaceId, flowId)}/${id}`, { cronExpression });
}

export function enableSchedule(workspaceId: string, flowId: string, id: string): Promise<Schedule> {
  return api.post<Schedule>(`${base(workspaceId, flowId)}/${id}/enable`, {});
}

export function disableSchedule(workspaceId: string, flowId: string, id: string): Promise<Schedule> {
  return api.post<Schedule>(`${base(workspaceId, flowId)}/${id}/disable`, {});
}

export function deleteSchedule(workspaceId: string, flowId: string, id: string): Promise<void> {
  return api.del<void>(`${base(workspaceId, flowId)}/${id}`);
}

export function previewSchedule(
  workspaceId: string,
  flowId: string,
  cron: string,
  count = 5,
): Promise<SchedulePreview> {
  return api.get<SchedulePreview>(
    `${base(workspaceId, flowId)}/preview?cron=${encodeURIComponent(cron)}&count=${count}`,
  );
}
