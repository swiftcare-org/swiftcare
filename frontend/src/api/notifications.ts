import { apiRequest } from './client';

export type ActivityType = 'PatientCheckedIn' | 'PatientCalled' | 'ConsultationCompleted';
export type ActivityView = 'all' | 'today';

// One department event. The service stores identifiers only, never a patient name.
export interface ActivityEntry {
  id: string;
  type: ActivityType;
  patientId: string;
  occurredAt: string;
  // Check-in only.
  isNewPatient?: boolean | null;
  // Patient called only.
  queueNumber?: string | null;
  doctorName?: string | null;
  roomNumber?: string | null;
}

// Newest first. The server caps how many entries one request returns.
export function listActivity(view: ActivityView = 'all'): Promise<ActivityEntry[]> {
  return apiRequest<ActivityEntry[]>(view === 'today' ? '/api/notifications/today' : '/api/notifications');
}
