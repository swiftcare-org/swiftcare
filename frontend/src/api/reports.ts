import { apiRequest } from './client';

export interface RoomPatientCount {
  roomNumber: string;
  patients: number;
}

export interface DiagnosisCount {
  diagnosis: string;
  count: number;
}

// One clinic day of department activity, from NotificationService.
// totalPatients === newPatients + returningPatients.
export interface DailyActivityReport {
  date: string;
  totalPatients: number;
  newPatients: number;
  returningPatients: number;
  // The clinic's rooms are always listed, with 0 for a room that saw no patients.
  patientsPerRoom: RoomPatientCount[];
  // At most five, most common first.
  topDiagnoses: DiagnosisCount[];
}

// The same clinic day of prescriptions, from PrescriptionService.
// totalWritten === totalDispensed + totalPending.
export interface DailyPrescriptionReport {
  date: string;
  totalWritten: number;
  totalDispensed: number;
  totalPending: number;
}

/** `date` is "yyyy-MM-dd", the value of a date input. */
export function getDailyActivityReport(date: string): Promise<DailyActivityReport> {
  return apiRequest<DailyActivityReport>(`/api/reports/daily?date=${encodeURIComponent(date)}`);
}

/** `date` is "yyyy-MM-dd", the value of a date input. */
export function getDailyPrescriptionReport(date: string): Promise<DailyPrescriptionReport> {
  return apiRequest<DailyPrescriptionReport>(`/api/prescriptions/report/daily?date=${encodeURIComponent(date)}`);
}
