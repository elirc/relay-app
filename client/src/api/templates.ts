import { api } from './client';
import type { FlowDetail } from './types';

export interface FlowTemplateStep {
  name: string;
  connectorKey: string;
  action: string;
  configJson: string;
  maxAttempts: number;
  backoffSeconds: number;
}

export interface FlowTemplate {
  id: string;
  name: string;
  description: string;
  category: string;
  triggerConnectorKey: string;
  steps: FlowTemplateStep[];
}

export function listTemplates(): Promise<FlowTemplate[]> {
  return api.get<FlowTemplate[]>('/api/flow-templates');
}

export function getTemplate(id: string): Promise<FlowTemplate> {
  return api.get<FlowTemplate>(`/api/flow-templates/${id}`);
}

export function instantiateTemplate(workspaceId: string, templateId: string): Promise<FlowDetail> {
  return api.post<FlowDetail>(`/api/workspaces/${workspaceId}/flows/from-template/${templateId}`, {});
}
