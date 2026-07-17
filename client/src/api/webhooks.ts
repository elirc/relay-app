import { api } from './client';
import type { Webhook } from './types';

const base = (workspaceId: string, flowId: string) =>
  `/api/workspaces/${workspaceId}/flows/${flowId}/webhooks`;

export function listWebhooks(workspaceId: string, flowId: string): Promise<Webhook[]> {
  return api.get<Webhook[]>(base(workspaceId, flowId));
}

export function createWebhook(workspaceId: string, flowId: string): Promise<Webhook> {
  return api.post<Webhook>(base(workspaceId, flowId), {});
}

export function deleteWebhook(workspaceId: string, flowId: string, id: string): Promise<void> {
  return api.del<void>(`${base(workspaceId, flowId)}/${id}`);
}
