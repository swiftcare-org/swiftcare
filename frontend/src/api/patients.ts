import { apiRequest } from './client';

export type Gender = 'Male' | 'Female' | 'Other';

export type BloodGroup = 'A+' | 'A-' | 'B+' | 'B-' | 'O+' | 'O-' | 'AB+' | 'AB-';

export interface RegisterPatientRequestBody {
  nic: string;
  fullName: string;
  dateOfBirth: string;
  gender: Gender;
  address: string;
  phoneNumber: string;
  bloodGroup: BloodGroup;
}

export interface RegisteredPatient {
  patientId: string;
  createdAt: string;
}

export function registerPatient(request: RegisterPatientRequestBody): Promise<RegisteredPatient> {
  return apiRequest<RegisteredPatient>('/api/patients', {
    method: 'POST',
    body: request,
  });
}

export interface PatientSearchResult {
  patientId: string;
  fullName: string;
  nic: string;
  phoneNumber: string;
  bloodGroup: BloodGroup;
}

export function searchPatients(term: string): Promise<PatientSearchResult[]> {
  return apiRequest<PatientSearchResult[]>(`/api/patients/search?q=${encodeURIComponent(term)}`);
}

export interface PatientProfile {
  patientId: string;
  fullName: string;
  nic: string;
  dateOfBirth: string;
  gender: Gender;
  address: string;
  phoneNumber: string;
  bloodGroup: BloodGroup;
  registeredAt: string;
}

export function getPatient(patientId: string): Promise<PatientProfile> {
  return apiRequest<PatientProfile>(`/api/patients/${encodeURIComponent(patientId)}`);
}

export interface UpdatePatientRequestBody {
  address: string;
  phoneNumber: string;
  bloodGroup: BloodGroup;
}

export function updatePatient(
  patientId: string,
  request: UpdatePatientRequestBody,
): Promise<PatientProfile> {
  return apiRequest<PatientProfile>(`/api/patients/${encodeURIComponent(patientId)}`, {
    method: 'PUT',
    body: request,
  });
}

export interface CheckInPatientAcceptedResponse {
  message: string;
}

export function checkInPatient(patientId: string): Promise<CheckInPatientAcceptedResponse> {
  return apiRequest<CheckInPatientAcceptedResponse>(
    `/api/patients/${encodeURIComponent(patientId)}/check-in`,
    { method: 'POST' },
  );
}
