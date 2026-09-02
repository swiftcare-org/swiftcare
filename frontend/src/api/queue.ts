import { apiRequest } from './client';

export interface PatientQueueStatus {
  isCheckedIn: boolean;
  queueNumber: string | null;
}

export function getPatientQueueStatus(patientId: string): Promise<PatientQueueStatus> {
  return apiRequest<PatientQueueStatus>(
    `/api/queue/today/patient/${encodeURIComponent(patientId)}`,
  );
}
