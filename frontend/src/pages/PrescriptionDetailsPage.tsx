import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import {
  dispensePrescription,
  getPrescriptionByQueueId,
  type Prescription,
} from '../api/prescriptions';
import { useAuth } from '../auth/useAuth';
import type { UserRole } from '../auth/types';
import { DashboardShell } from '../dashboards/DashboardShell';

type LoadState = 'loading' | 'loaded' | 'error';
type DispenseState = 'idle' | 'submitting';

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

function loadErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 404) {
      return 'A prescription has not been created for this queue entry.';
    }

    if (error.status === 401 || error.status === 403) {
      return 'You are not authorized to view this prescription.';
    }
  }

  return 'Unable to load the prescription. Please try again.';
}

export function PrescriptionDetailsPage() {
  const { queueId } = useParams<{ queueId: string }>();
  const { user } = useAuth();
  const [loadState, setLoadState] = useState<LoadState>(queueId ? 'loading' : 'error');
  const [prescription, setPrescription] = useState<Prescription | null>(null);
  const [loadMessage, setLoadMessage] = useState<string | null>(
    queueId ? null : 'The queue reference is unavailable.',
  );
  const [dispenseState, setDispenseState] = useState<DispenseState>('idle');
  const [dispenseMessage, setDispenseMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!queueId) {
      return;
    }

    let disposed = false;
    const selectedQueueId = queueId;

    async function loadPrescription() {
      try {
        const loaded = await getPrescriptionByQueueId(selectedQueueId);
        if (!disposed) {
          setPrescription(loaded);
          setLoadState('loaded');
        }
      } catch (error) {
        if (!disposed) {
          setLoadMessage(loadErrorMessage(error));
          setLoadState('error');
        }
      }
    }

    void loadPrescription();

    return () => {
      disposed = true;
    };
  }, [queueId]);

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

      {loadState === 'loaded' && prescription && (
        <section className="mt-6 space-y-6 border border-slate-300 bg-white px-6 py-6">
          <div className="flex flex-wrap items-start justify-between gap-4 border-b border-slate-200 pb-4">
            <div>
              <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">
                Prescribed by
              </p>
              <h2 className="mt-1 text-xl font-semibold text-slate-900">
                {prescription.doctorName}
              </h2>
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

          <div>
            <h3 className="text-sm font-bold uppercase tracking-[0.12em] text-slate-700">
              Medicines
            </h3>
            <ul className="mt-3 space-y-3">
              {prescription.medicines.map((medicine) => (
                <li key={medicine.id} className="border border-slate-300 px-4 py-3 text-sm">
                  <p className="font-semibold text-slate-900">{medicine.medicineName}</p>
                  <p className="mt-1 text-slate-700">
                    {medicine.dosage}, {medicine.frequency}, {medicine.duration}
                  </p>
                  {medicine.instructions && (
                    <p className="mt-1 text-slate-600">{medicine.instructions}</p>
                  )}
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
