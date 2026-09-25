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
  status: 'PENDING' | 'DISPENSED';
  createdAt: string;
  medicines: PrescriptionMedicine[];
  dispensedBy: string | null;
  dispensedAt: string | null;
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

export function getPrescriptionByQueueId(queueId: string): Promise<Prescription> {
  return apiRequest<Prescription>(
    `/api/prescriptions/queue/${encodeURIComponent(queueId)}`,
  );
}

export function dispensePrescription(prescriptionId: string): Promise<Prescription> {
  return apiRequest<Prescription>(
    `/api/prescriptions/${encodeURIComponent(prescriptionId)}/dispense`,
    { method: 'PUT' },
  );
}

export function addPrescriptionMedicine(
  prescriptionId: string,
  medicine: PrescriptionMedicineInput,
): Promise<Prescription> {
  return apiRequest<Prescription>(
    `/api/prescriptions/${encodeURIComponent(prescriptionId)}/items`,
    {
      method: 'POST',
      body: medicine,
    },
  );
}

export function removePrescriptionMedicine(
  prescriptionId: string,
  medicineId: string,
): Promise<Prescription> {
  return apiRequest<Prescription>(
    `/api/prescriptions/${encodeURIComponent(prescriptionId)}/items/${encodeURIComponent(medicineId)}`,
    { method: 'DELETE' },
  );
}
