import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
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
import { DashboardShell } from '../dashboards/DashboardShell';

type LoadState = 'loading' | 'loaded' | 'error';
type DispenseState = 'idle' | 'submitting';

interface CounterContext {
  queueEntry: TodayQueueEntry;
  patientName: string;
}

const CLINIC_TIME_ZONE = 'Asia/Colombo';

function dashboardRoute(role: UserRole | undefined): string {
  if (role === 'Receptionist') {
    return '/reception/queue';
  }

  if (role === 'Admin') {
    return '/admin';
  }

  return '/doctor';
}

function formatDispensedTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return 'time unavailable';
  }

  return new Intl.DateTimeFormat('en-LK', {
    hour: 'numeric',
    minute: '2-digit',
    timeZone: CLINIC_TIME_ZONE,
  }).format(date);
}

function formatPrescriptionDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return 'Date unavailable';
  }

  return new Intl.DateTimeFormat('en-LK', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: CLINIC_TIME_ZONE,
  }).format(date);
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
      || dispenseState !== 'idle'
    ) {
      return;
    }

    setDispenseState('submitting');
    setDispenseMessage(null);

    try {
      const updated = await dispensePrescription(prescription.id);
      setPrescription(updated);
    } catch (error) {
      setDispenseMessage(
        error instanceof ApiError && error.status >= 400 && error.status < 500
          ? error.message
          : 'Unable to dispense the prescription. Please try again.',
      );
    } finally {
      setDispenseState('idle');
    }
  }

  return (
    <DashboardShell sectionLabel="Prescription Details">
      <Link
        to={dashboardRoute(user?.role)}
        className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
      >
        ← Back
      </Link>

      {loadState === 'loading' && (
        <p className="mt-6 text-sm text-slate-600">Loading prescription…</p>
      )}

      {loadState === 'error' && (
        <p className="mt-6 border-l-4 border-red-700 bg-red-50 px-4 py-3 text-sm text-red-900" role="alert">
          {loadMessage}
        </p>
      )}

      {loadState === 'loaded' && (
        <section className="mt-6 space-y-6 border border-slate-300 bg-white px-6 py-6">
          {counterContext && (
            <dl className="grid gap-4 border-b border-slate-200 pb-5 text-sm sm:grid-cols-2 lg:grid-cols-4">
              <div>
                <dt className="font-semibold text-slate-600">Patient</dt>
                <dd className="mt-1 text-slate-900">{counterContext.patientName}</dd>
              </div>
              <div>
                <dt className="font-semibold text-slate-600">Queue number</dt>
                <dd className="mt-1 text-slate-900">{counterContext.queueEntry.queueNumber}</dd>
              </div>
              <div>
                <dt className="font-semibold text-slate-600">Doctor</dt>
                <dd className="mt-1 text-slate-900">
                  {prescription?.doctorName ?? counterContext.queueEntry.doctorName ?? 'Not assigned'}
                </dd>
              </div>
              <div>
                <dt className="font-semibold text-slate-600">Room</dt>
                <dd className="mt-1 text-slate-900">
                  {counterContext.queueEntry.roomNumber ?? 'Not assigned'}
                </dd>
              </div>
            </dl>
          )}

          {!prescription && (
            <p
              className="border-l-4 border-blue-700 bg-blue-50 px-4 py-3 text-sm font-semibold text-blue-900"
              role="status"
            >
              No prescription recorded yet. Doctor may still be writing it.
            </p>
          )}

          {prescription && (
            <>
              <div className="flex flex-wrap items-start justify-between gap-4 border-b border-slate-200 pb-4">
                <div>
                  <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">
                    Prescription date
                  </p>
                  <h2 className="mt-1 text-xl font-semibold text-slate-900">
                    {formatPrescriptionDate(prescription.createdAt)}
                  </h2>
                  {!counterContext && (
                    <p className="mt-1 text-sm text-slate-600">
                      Prescribed by {prescription.doctorName}
                    </p>
                  )}
                </div>
                <span
                  className={`border px-3 py-1 text-xs font-bold ${
                    prescription.status === 'DISPENSED'
                      ? 'border-emerald-300 bg-emerald-50 text-emerald-900'
                      : 'border-amber-300 bg-amber-50 text-amber-900'
                  }`}
                >
                  {prescription.status}
                </span>
              </div>

              {!counterContext && (
                <dl className="grid gap-3 text-sm sm:grid-cols-2">
                  <div>
                    <dt className="font-semibold text-slate-600">Queue reference</dt>
                    <dd className="mt-1 break-all text-slate-900">{prescription.queueId}</dd>
                  </div>
                  <div>
                    <dt className="font-semibold text-slate-600">Patient reference</dt>
                    <dd className="mt-1 break-all text-slate-900">{prescription.patientId}</dd>
                  </div>
                </dl>
              )}

              <div>
                <h3 className="text-sm font-bold uppercase tracking-[0.12em] text-slate-700">
                  Medicines
                </h3>
                <ul className="mt-3 space-y-3">
                  {prescription.medicines.map((medicine) => (
                    <li key={medicine.id} className="border border-slate-300 px-4 py-3 text-sm">
                      <p className="font-semibold text-slate-900">{medicine.medicineName}</p>
                      <dl className="mt-3 grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
                        <div>
                          <dt className="font-semibold text-slate-600">Dosage</dt>
                          <dd className="mt-1 text-slate-900">{medicine.dosage}</dd>
                        </div>
                        <div>
                          <dt className="font-semibold text-slate-600">Frequency</dt>
                          <dd className="mt-1 text-slate-900">{medicine.frequency}</dd>
                        </div>
                        <div>
                          <dt className="font-semibold text-slate-600">Duration</dt>
                          <dd className="mt-1 text-slate-900">{medicine.duration}</dd>
                        </div>
                        <div>
                          <dt className="font-semibold text-slate-600">Instructions</dt>
                          <dd className="mt-1 text-slate-900">{medicine.instructions ?? '—'}</dd>
                        </div>
                      </dl>
                    </li>
                  ))}
                </ul>
              </div>

              {prescription.status === 'DISPENSED' && (
                <p
                  className="border-l-4 border-emerald-700 bg-emerald-50 px-4 py-3 text-sm font-semibold text-emerald-900"
                  role="status"
                >
                  {prescription.dispensedBy && prescription.dispensedAt
                    ? `Dispensed by ${prescription.dispensedBy} at ${formatDispensedTime(prescription.dispensedAt)}`
                    : 'Prescription dispensed'}
                </p>
              )}

              {user?.role === 'Receptionist' && prescription.status === 'PENDING' && (
                <button
                  type="button"
                  onClick={() => void handleDispense()}
                  disabled={dispenseState !== 'idle'}
                  className="bg-brand-blue px-5 py-3 text-xs font-bold uppercase tracking-[0.12em] text-white hover:bg-brand-blue-dark disabled:cursor-not-allowed disabled:bg-slate-400"
                >
                  {dispenseState === 'submitting' ? 'Dispensing…' : 'Mark as Dispensed'}
                </button>
              )}
            </>
          )}

          {dispenseMessage && (
            <p className="border-l-4 border-red-700 bg-red-50 px-4 py-3 text-sm text-red-900" role="alert">
              {dispenseMessage}
            </p>
          )}
        </section>
      )}
    </DashboardShell>
  );
}
