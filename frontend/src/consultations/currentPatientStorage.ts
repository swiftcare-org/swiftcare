import type { CalledPatient } from '../api/queue';

export interface CurrentPatient extends CalledPatient {
  patientName: string;
}

const CLINIC_TIME_ZONE = 'Asia/Colombo';
const CURRENT_PATIENT_STORAGE_PREFIX = 'swiftcare.doctor.current-patient';

const clinicDateFormatter = new Intl.DateTimeFormat('en-CA', {
  day: '2-digit',
  month: '2-digit',
  timeZone: CLINIC_TIME_ZONE,
  year: 'numeric',
});

function clinicDateKey(): string {
  const parts = Object.fromEntries(
    clinicDateFormatter
      .formatToParts(new Date())
      .filter((part) => part.type !== 'literal')
      .map((part) => [part.type, part.value]),
  );

  return `${parts.year}-${parts.month}-${parts.day}`;
}

function currentPatientStorageKey(userId: string): string {
  return `${CURRENT_PATIENT_STORAGE_PREFIX}.${userId}.${clinicDateKey()}`;
}

function isCurrentPatient(value: unknown): value is CurrentPatient {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const patient = value as Partial<CurrentPatient>;
  return (
    typeof patient.queueId === 'string' &&
    typeof patient.patientId === 'string' &&
    typeof patient.queueNumber === 'string' &&
    patient.status === 'IN_CONSULTATION' &&
    typeof patient.doctorId === 'string' &&
    typeof patient.doctorName === 'string' &&
    typeof patient.roomNumber === 'string' &&
    typeof patient.calledAt === 'string' &&
    typeof patient.patientName === 'string'
  );
}

export function readStoredCurrentPatient(userId: string | undefined): CurrentPatient | null {
  if (!userId) {
    return null;
  }

  const storageKey = currentPatientStorageKey(userId);

  try {
    const raw = sessionStorage.getItem(storageKey);
    if (!raw) {
      return null;
    }

    const storedPatient: unknown = JSON.parse(raw);
    if (isCurrentPatient(storedPatient)) {
      return storedPatient;
    }

    sessionStorage.removeItem(storageKey);
  } catch {
    // Browser storage can be unavailable; callers handle a missing assignment.
  }

  return null;
}

export function storeCurrentPatient(
  userId: string | undefined,
  patient: CurrentPatient,
): void {
  if (!userId) {
    return;
  }

  try {
    sessionStorage.setItem(currentPatientStorageKey(userId), JSON.stringify(patient));
  } catch {
    // The successful call remains visible for this render even if storage is unavailable.
  }
}
