import { getCompletedConsultations } from '../api/consultations';
import { getPatient } from '../api/patients';
import { getPrescriptionByQueueId } from '../api/prescriptions';
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

async function hasRecordedOutcome(queueId: string): Promise<boolean> {
  try {
    await getPrescriptionByQueueId(queueId);
    return true;
  } catch {
    // 404 means nothing is recorded. Any other failure is treated the same way, because a
    // temporary lookup problem must not hide unfinished clinical work.
    return false;
  }
}

export async function findPendingPrescriptionContext(): Promise<PrescriptionContext | null> {
  const currentPatient = await getCurrentPatient();
  for (let page = 0; ; page += 1) {
    const consultations = await getCompletedConsultations(page);
    for (const consultation of consultations) {
      // MedicalRecordService commits COMPLETE before publishing to Kafka. When
      // publishing has not succeeded yet, QueueService still owns the same active
      // assignment and the doctor must retry completion before prescribing.
      if (currentPatient?.queueId === consultation.queueId) {
        continue;
      }

      // A consultation recorded as needing no prescription is resolved too. That decision is
      // not in the patient's prescription history, so it is looked up by the queue entry.
      if (await hasRecordedOutcome(consultation.queueId)) {
        continue;
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
    if (consultations.length < 50) return null;
  }
}
