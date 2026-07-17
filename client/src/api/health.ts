import { api } from './client';

export interface HealthResponse {
  status: string;
  service: string;
  timestampUtc: string;
}

export function getHealth(signal?: AbortSignal): Promise<HealthResponse> {
  return api.get<HealthResponse>('/health', signal);
}
