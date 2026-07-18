import { api } from './client';
import type { PagedResult, Webhook, WebhookDelivery } from './types';

export interface SigningSecretResponse {
  signingSecret: string;
  timestampHeader: string;
  signatureHeader: string;
}

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

export function generateSigningSecret(
  workspaceId: string,
  flowId: string,
  id: string,
): Promise<SigningSecretResponse> {
  return api.post<SigningSecretResponse>(`${base(workspaceId, flowId)}/${id}/signing-secret`, {});
}

export function deleteSigningSecret(
  workspaceId: string,
  flowId: string,
  id: string,
): Promise<void> {
  return api.del<void>(`${base(workspaceId, flowId)}/${id}/signing-secret`);
}

export function listDeliveries(
  workspaceId: string,
  flowId: string,
  id: string,
): Promise<PagedResult<WebhookDelivery>> {
  return api.get<PagedResult<WebhookDelivery>>(
    `${base(workspaceId, flowId)}/${id}/deliveries?page=1&pageSize=50`,
  );
}
