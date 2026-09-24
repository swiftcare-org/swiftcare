import { getLatestCompletedConsultation } from '../api/consultations';
import { getPatient } from '../api/patients';
import { getPatientPrescriptions } from '../api/prescriptions';
import { getCurrentPatient } from '../api/queue';

export interface PrescriptionContext {
  completed?: boolean;
  consultationId: string;
  queueId: string;
  patientId: string;
  patientName?: string;
  queueNumber?: string;
}

export function isPrescriptionContext(value: unknown): value is PrescriptionContext {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as Partial<PrescriptionContext>;
  return Boolean(candidate.consultationId && candidate.queueId && candidate.patientId);
}

export async function findPendingPrescriptionContext(): Promise<PrescriptionContext | null> {
  const [consultation, currentPatient] = await Promise.all([
    getLatestCompletedConsultation(),
    getCurrentPatient(),
  ]);
  if (!consultation) {
    return null;
  }

  // MedicalRecordService commits COMPLETE before publishing to Kafka. When
  // publishing has not succeeded yet, QueueService still owns the same active
  // assignment and the doctor must retry completion before prescribing.
  if (currentPatient?.queueId === consultation.queueId) {
    return null;
  }

  const prescriptions = await getPatientPrescriptions(consultation.patientId);
  if (prescriptions.some((item) => item.consultationId === consultation.consultationId)) {
    return null;
  }

  let patientName: string | undefined;
  try {
    patientName = (await getPatient(consultation.patientId)).fullName;
  } catch {
    // The identifiers are sufficient to reopen the prescription. A temporary
    // patient-name lookup failure must not hide unfinished clinical work.
  }

  return {
    completed: true,
    consultationId: consultation.consultationId,
    queueId: consultation.queueId,
    patientId: consultation.patientId,
    patientName,
  };
}
