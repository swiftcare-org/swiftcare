import { apiRequest } from './client';

export type AllergySeverity = 'Severe' | 'Moderate' | 'Mild';

export interface Allergy {
  allergyId: string;
  allergyName: string;
  severity: AllergySeverity;
  notes: string | null;
  recordedAt: string;
}

export interface AllergyRequestBody {
  allergyName: string;
  severity: AllergySeverity;
  notes: string | null;
}

export function getAllergies(patientId: string): Promise<Allergy[]> {
  return apiRequest<Allergy[]>(`/api/patients/${encodeURIComponent(patientId)}/allergies`);
}

export function addAllergy(patientId: string, request: AllergyRequestBody): Promise<Allergy> {
  return apiRequest<Allergy>(`/api/patients/${encodeURIComponent(patientId)}/allergies`, {
    method: 'POST',
    body: request,
  });
}

export function updateAllergy(
  patientId: string,
  allergyId: string,
  request: AllergyRequestBody,
): Promise<Allergy> {
  return apiRequest<Allergy>(
    `/api/patients/${encodeURIComponent(patientId)}/allergies/${encodeURIComponent(allergyId)}`,
    { method: 'PUT', body: request },
  );
}

export function removeAllergy(patientId: string, allergyId: string): Promise<void> {
  return apiRequest<void>(
    `/api/patients/${encodeURIComponent(patientId)}/allergies/${encodeURIComponent(allergyId)}`,
    { method: 'DELETE' },
  );
}
