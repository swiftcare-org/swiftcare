import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getPatient } from '../api/patients';
import {
  dispensePrescription,
  getPrescriptionByQueueId,
  type Prescription,
} from '../api/prescriptions';
import { getTodayQueue, type TodayQueueEntry } from '../api/queue';
import { useAuth } from '../auth/useAuth';
import type { UserRole } from '../auth/types';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { ConfirmPanel } from '../components/ui/ConfirmPanel';
import { LoadingText } from '../components/ui/Feedback';
import { SectionCard } from '../components/ui/SectionCard';
import { StatusBadge, type StatusBadgeTone } from '../components/ui/StatusBadge';
import { DashboardShell } from '../dashboards/DashboardShell';
import { formatDate, formatDateTime, formatTime } from '../lib/format';

type LoadState = 'loading' | 'loaded' | 'error';
type DispenseState = 'idle' | 'confirming' | 'submitting';

interface CounterContext {
  queueEntry: TodayQueueEntry;
  patientName: string;
}

const STATUS_TONES: Record<Prescription['status'], StatusBadgeTone> = {
  PENDING: 'warning',
  DISPENSED: 'success',
  NOT_REQUIRED: 'neutral',
};

const DETAIL_TERM_CLASS_NAME = 'text-xs font-medium text-slate-500';
const DETAIL_VALUE_CLASS_NAME = 'mt-0.5 break-words text-slate-900';

function backTarget(role: UserRole | undefined): { to: string; destination: string } {
  if (role === 'Receptionist') {
    return { to: '/reception/queue', destination: 'Queue' };
  }

  if (role === 'Admin') {
    return { to: '/admin', destination: 'Dashboard' };
  }

  return { to: '/doctor', destination: 'Dashboard' };
}

async function loadPrescriptionOrNull(queueId: string): Promise<Prescription | null> {
  try {
    return await getPrescriptionByQueueId(queueId);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return null;
    }

    throw error;
  }
}

function loadErrorMessage(error: unknown): string {
  if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
    return 'You are not authorized to view this prescription.';
  }

  return 'Unable to load the prescription. Please try again.';
}

export function PrescriptionDetailsPage() {
  const { queueId } = useParams<{ queueId: string }>();
  const { user } = useAuth();
  const [loadState, setLoadState] = useState<LoadState>(queueId ? 'loading' : 'error');
  const [counterContext, setCounterContext] = useState<CounterContext | null>(null);
  const [prescription, setPrescription] = useState<Prescription | null>(null);
  const [loadMessage, setLoadMessage] = useState<string | null>(
    queueId ? null : 'The queue reference is unavailable.',
  );
  const [dispenseState, setDispenseState] = useState<DispenseState>('idle');
  const [dispenseMessage, setDispenseMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!queueId || !user?.role) {
      return;
    }

    let disposed = false;
    const selectedQueueId = queueId;
    const selectedRole = user.role;

    async function loadDetails() {
      try {
        if (selectedRole === 'Receptionist') {
          const queue = await getTodayQueue();
          const queueEntry = queue.find((entry) => entry.queueId === selectedQueueId);
          if (!queueEntry) {
            throw new Error('Queue entry was not found in the current clinic day.');
          }

          const [patient, loadedPrescription] = await Promise.all([
            getPatient(queueEntry.patientId),
            loadPrescriptionOrNull(selectedQueueId),
          ]);

          if (!disposed) {
            setCounterContext({ queueEntry, patientName: patient.fullName });
            setPrescription(loadedPrescription);
            setLoadState('loaded');
          }
          return;
        }

        const loadedPrescription = await loadPrescriptionOrNull(selectedQueueId);
        if (!disposed) {
          setPrescription(loadedPrescription);
          setLoadState('loaded');
        }
      } catch (error) {
        if (!disposed) {
          setLoadMessage(loadErrorMessage(error));
          setLoadState('error');
        }
      }
    }

    void loadDetails();

    return () => {
      disposed = true;
    };
  }, [queueId, user?.role]);

  async function handleDispense() {
    if (
      !prescription
      || prescription.status !== 'PENDING'
      || user?.role !== 'Receptionist'
      || dispenseState === 'submitting'
    ) {
      return;
    }

    setDispenseState('submitting');
    setDispenseMessage(null);

    try {
      const updated = await dispensePrescription(prescription.id);
      setPrescription(updated);
      setDispenseState('idle');
    } catch (error) {
      setDispenseMessage(
        error instanceof ApiError && error.status >= 400 && error.status < 500
          ? error.message
          : 'Unable to dispense the prescription. Please try again.',
      );
      // Back to the confirm step, so the reason is shown beside the action that failed.
      setDispenseState('confirming');
    }
  }

  // Without a signed-in role the load never starts, so say so instead of waiting forever.
  const effectiveLoadState: LoadState = user?.role ? loadState : 'error';
  const effectiveLoadMessage = user?.role
    ? loadMessage
    : 'Your session could not be confirmed. Please sign in again.';

  return (
    <DashboardShell sectionLabel="Prescription Details" backLink={backTarget(user?.role)}>
      {effectiveLoadState === 'loading' && <LoadingText>Loading prescription…</LoadingText>}

      {effectiveLoadState === 'error' && (
        <div data-testid="prescription-load-error">
          <Banner tone="error" title="Prescription Unavailable" role="alert">
            {effectiveLoadMessage}
          </Banner>
        </div>
      )}

      {effectiveLoadState === 'loaded' && (
        <div className="space-y-6" data-testid="prescription-details">
          {counterContext && (
            <SectionCard title="Patient at the Counter">
              <dl className="grid gap-x-6 gap-y-4 text-sm sm:grid-cols-2 lg:grid-cols-4" data-testid="counter-details">
                <div>
                  <dt className={DETAIL_TERM_CLASS_NAME}>Patient</dt>
                  <dd className={DETAIL_VALUE_CLASS_NAME}>{counterContext.patientName}</dd>
                </div>
                <div>
                  <dt className={DETAIL_TERM_CLASS_NAME}>Queue number</dt>
                  <dd className={DETAIL_VALUE_CLASS_NAME}>{counterContext.queueEntry.queueNumber}</dd>
                </div>
                <div>
                  <dt className={DETAIL_TERM_CLASS_NAME}>Doctor</dt>
                  <dd className={DETAIL_VALUE_CLASS_NAME}>
                    {prescription?.doctorName ?? counterContext.queueEntry.doctorName ?? 'Not assigned'}
                  </dd>
                </div>
                <div>
                  <dt className={DETAIL_TERM_CLASS_NAME}>Room</dt>
                  <dd className={DETAIL_VALUE_CLASS_NAME}>
                    {counterContext.queueEntry.roomNumber ?? 'Not assigned'}
                  </dd>
                </div>
              </dl>
            </SectionCard>
          )}

          {!prescription && (
            <Banner tone="info" role="status">
              No prescription recorded yet. Doctor may still be writing it.
            </Banner>
          )}

          {prescription && (
            <SectionCard
              eyebrow={prescription.status === 'NOT_REQUIRED' ? 'Recorded' : 'Prescription date'}
              title={<span data-testid="prescription-date">{formatDateTime(prescription.createdAt)}</span>}
              description={
                !counterContext && prescription.status !== 'NOT_REQUIRED'
                  ? `Prescribed by ${prescription.doctorName}`
                  : undefined
              }
              actions={
                <StatusBadge
                  tone={STATUS_TONES[prescription.status]}
                  data-testid="prescription-status"
                >
                  {prescription.status === 'NOT_REQUIRED' ? 'No prescription required' : prescription.status}
                </StatusBadge>
              }
            >
              <div className="space-y-5">
                {prescription.status === 'NOT_REQUIRED' && (
                  <Banner tone="neutral" role="status">
                    <span data-testid="no-prescription-details">
                      No prescription required. Recorded by {prescription.doctorName} on{' '}
                      {formatDate(prescription.createdAt)} at {formatTime(prescription.createdAt)}.
                    </span>
                  </Banner>
                )}

                {prescription.status !== 'NOT_REQUIRED' && (
                <div>
                  <h3 className="text-base font-semibold text-slate-900">Medicines</h3>
                  <ul className="mt-3 space-y-3">
                    {prescription.medicines.map((medicine) => (
                      <li key={medicine.id} className="rounded-md border border-slate-200 px-4 py-3 text-sm">
                        <p className="break-words font-semibold text-slate-900">{medicine.medicineName}</p>
                        <dl className="mt-3 grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
                          <div>
                            <dt className={DETAIL_TERM_CLASS_NAME}>Dosage</dt>
                            <dd className={DETAIL_VALUE_CLASS_NAME}>{medicine.dosage}</dd>
                          </div>
                          <div>
                            <dt className={DETAIL_TERM_CLASS_NAME}>Frequency</dt>
                            <dd className={DETAIL_VALUE_CLASS_NAME}>{medicine.frequency}</dd>
                          </div>
                          <div>
                            <dt className={DETAIL_TERM_CLASS_NAME}>Duration</dt>
                            <dd className={DETAIL_VALUE_CLASS_NAME}>{medicine.duration}</dd>
                          </div>
                          <div>
                            <dt className={DETAIL_TERM_CLASS_NAME}>Instructions</dt>
                            <dd className={DETAIL_VALUE_CLASS_NAME}>{medicine.instructions ?? '-'}</dd>
                          </div>
                        </dl>
                      </li>
                    ))}
                  </ul>
                </div>
                )}

                {prescription.status === 'DISPENSED' && (
                  <Banner tone="success" role="status">
                    {prescription.dispensedBy && prescription.dispensedAt
                      ? `Dispensed by ${prescription.dispensedBy} on ${formatDate(prescription.dispensedAt)} at ${formatTime(prescription.dispensedAt)}`
                      : 'Prescription dispensed'}
                  </Banner>
                )}

                {user?.role === 'Receptionist' && prescription.status === 'PENDING' && (
                  dispenseState === 'idle' ? (
                    <div>
                      <p className="text-sm text-slate-600">
                        Mark this once every medicine has been handed to the patient.
                      </p>
                      <Button className="mt-3" onClick={() => setDispenseState('confirming')}>
                        Mark as Dispensed
                      </Button>
                    </div>
                  ) : (
                    <ConfirmPanel
                      labelId="confirm-dispense-title"
                      title="Mark as Dispensed?"
                      tone="primary"
                      confirmLabel="Confirm Dispense"
                      busyLabel="Dispensing…"
                      busy={dispenseState === 'submitting'}
                      error={dispenseMessage}
                      onConfirm={() => void handleDispense()}
                      onCancel={() => {
                        setDispenseState('idle');
                        setDispenseMessage(null);
                      }}
                    >
                      This records that every medicine was handed over. The prescription cannot be changed
                      afterwards.
                    </ConfirmPanel>
                  )
                )}
              </div>
            </SectionCard>
          )}
        </div>
      )}
    </DashboardShell>
  );
}
