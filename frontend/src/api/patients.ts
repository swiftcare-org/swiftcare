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
