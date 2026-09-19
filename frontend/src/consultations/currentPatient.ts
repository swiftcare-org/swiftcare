import { getPatient } from '../api/patients';
import { getCurrentPatient, type CalledPatient } from '../api/queue';

export interface CurrentPatient extends CalledPatient {
  patientName: string;
}

export const PATIENT_UNAVAILABLE = 'Patient unavailable';

export async function resolveCurrentPatient(
  assignment: CalledPatient,
  knownPatientName?: string,
): Promise<CurrentPatient> {
  if (knownPatientName && knownPatientName !== PATIENT_UNAVAILABLE) {
    return { ...assignment, patientName: knownPatientName };
  }

  try {
    const patient = await getPatient(assignment.patientId);
    return { ...assignment, patientName: patient.fullName };
  } catch {
    return { ...assignment, patientName: PATIENT_UNAVAILABLE };
  }
}

export async function loadCurrentPatient(): Promise<CurrentPatient | null> {
  const assignment = await getCurrentPatient();
  return assignment ? resolveCurrentPatient(assignment) : null;
}
