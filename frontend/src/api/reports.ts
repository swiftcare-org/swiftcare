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

export interface WeekPatientCount {
  // 1 to 4. Week 4 runs from day 22 to the end of the month.
  week: number;
  patients: number;
}

// One calendar month of department activity, from NotificationService. Each patient is
// counted once, so totalPatients === newPatients + returningPatients, and the four
// weekly counts add up to it too.
export interface MonthlyActivityReport {
  month: string;
  totalPatients: number;
  newPatients: number;
  returningPatients: number;
  // At most five, most common first.
  topDiagnoses: DiagnosisCount[];
  // Always four weeks, with 0 for a week with no patients.
  weeklyBreakdown: WeekPatientCount[];
}

// The same month of prescriptions, from PrescriptionService.
export interface MonthlyPrescriptionReport {
  month: string;
  totalWritten: number;
  totalDispensed: number;
}

/** `month` is "yyyy-MM". */
export function getMonthlyActivityReport(month: string): Promise<MonthlyActivityReport> {
  return apiRequest<MonthlyActivityReport>(`/api/reports/monthly?month=${encodeURIComponent(month)}`);
}

/** `month` is "yyyy-MM". */
export function getMonthlyPrescriptionReport(month: string): Promise<MonthlyPrescriptionReport> {
  return apiRequest<MonthlyPrescriptionReport>(
    `/api/prescriptions/report/monthly?month=${encodeURIComponent(month)}`,
  );
}
