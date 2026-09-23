import { apiRequest } from './client';

export interface PrescriptionMedicineInput {
  medicineName: string;
  dosage: string;
  frequency: string;
  duration: string;
  instructions: string | null;
}

export interface CreatePrescriptionRequestBody {
  consultationId: string;
  queueId: string;
  patientId: string;
  medicines: PrescriptionMedicineInput[];
}

export interface PrescriptionMedicine extends PrescriptionMedicineInput {
  id: string;
  itemOrder: number;
}

export interface Prescription {
  id: string;
  consultationId: string;
  queueId: string;
  patientId: string;
  doctorId: string;
  doctorName: string;
  status: 'PENDING';
  createdAt: string;
  medicines: PrescriptionMedicine[];
}

export function createPrescription(
  request: CreatePrescriptionRequestBody,
): Promise<Prescription> {
  return apiRequest<Prescription>('/api/prescriptions', {
    method: 'POST',
    body: request,
  });
}

export function getPatientPrescriptions(patientId: string): Promise<Prescription[]> {
  return apiRequest<Prescription[]>(
    `/api/prescriptions/patient/${encodeURIComponent(patientId)}`,
  );
}
