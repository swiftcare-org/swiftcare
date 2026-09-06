import { apiRequest } from './client';

export interface ChronicCondition {
  conditionId: string;
  conditionName: string;
  dateDiagnosed: string;
  notes: string | null;
}

export interface ChronicConditionRequestBody {
  conditionName: string;
  dateDiagnosed: string;
  notes: string | null;
}

export function getConditions(patientId: string): Promise<ChronicCondition[]> {
  return apiRequest<ChronicCondition[]>(
    `/api/patients/${encodeURIComponent(patientId)}/conditions`,
  );
}

export function addCondition(
  patientId: string,
  request: ChronicConditionRequestBody,
): Promise<ChronicCondition> {
  return apiRequest<ChronicCondition>(
    `/api/patients/${encodeURIComponent(patientId)}/conditions`,
    { method: 'POST', body: request },
  );
}

export function removeCondition(patientId: string, conditionId: string): Promise<void> {
  return apiRequest<void>(
    `/api/patients/${encodeURIComponent(patientId)}/conditions/${encodeURIComponent(conditionId)}`,
    { method: 'DELETE' },
  );
}
