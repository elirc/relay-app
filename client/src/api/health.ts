import { api } from './client';

export interface HealthChecks {
  database: string;
}

export interface HealthResponse {
  status: string;
  service: string;
  version: string;
  checks: HealthChecks;
  timestampUtc: string;
}

export function getHealth(signal?: AbortSignal): Promise<HealthResponse> {
  return api.get<HealthResponse>('/health', signal);
}
