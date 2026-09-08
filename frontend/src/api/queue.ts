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

export type TodayQueueStatus = 'WAITING' | 'IN_CONSULTATION' | 'COMPLETED';

export interface TodayQueueEntry {
  queueId: string;
  patientId: string;
  queueNumber: string;
  checkedInAt: string;
  status: TodayQueueStatus;
  roomNumber: string | null;
  doctorName: string | null;
}

export function getTodayQueue(): Promise<TodayQueueEntry[]> {
  return apiRequest<TodayQueueEntry[]>('/api/queue/today');
}

export function getWaitingPool(): Promise<TodayQueueEntry[]> {
  return apiRequest<TodayQueueEntry[]>('/api/queue/today/waiting');
}
