import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getPatient } from '../api/patients';
import { getWaitingPool, type TodayQueueEntry } from '../api/queue';
import { DashboardShell } from './DashboardShell';

type WaitingPoolLoadState = 'loading' | 'loaded' | 'error';

interface WaitingPoolRow extends TodayQueueEntry {
  patientName: string;
}

const POLL_INTERVAL_MS = 5_000;
const CLINIC_TIME_ZONE = 'Asia/Colombo';
const PATIENT_UNAVAILABLE = 'Patient unavailable';

const checkInTimeFormatter = new Intl.DateTimeFormat('en-LK', {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  timeZone: CLINIC_TIME_ZONE,
});

function formatCheckInTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : checkInTimeFormatter.format(date);
}

async function addPatientNames(
  entries: TodayQueueEntry[],
  patientNameCache: Map<string, string>,
): Promise<WaitingPoolRow[]> {
  const unresolvedPatientIds = [
    ...new Set(
      entries
        .map((entry) => entry.patientId)
        .filter((patientId) => !patientNameCache.has(patientId)),
    ),
  ];

  const lookups = await Promise.allSettled(
    unresolvedPatientIds.map(async (patientId) => ({
      patientId,
      patient: await getPatient(patientId),
    })),
  );

  for (const lookup of lookups) {
    if (lookup.status === 'fulfilled') {
      patientNameCache.set(lookup.value.patientId, lookup.value.patient.fullName);
    }
  }

  return entries.map((entry) => ({
    ...entry,
    patientName: patientNameCache.get(entry.patientId) ?? PATIENT_UNAVAILABLE,
  }));
}

function waitingPoolErrorMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 403) {
    return 'You are not authorized to view the shared waiting pool.';
  }

  return 'Unable to load the shared waiting pool. Please try again.';
}

export function DoctorDashboard() {
  const [loadState, setLoadState] = useState<WaitingPoolLoadState>('loading');
  const [rows, setRows] = useState<WaitingPoolRow[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let disposed = false;
    let requestInFlight = false;
    let hasLoaded = false;
    const patientNameCache = new Map<string, string>();

    async function refreshWaitingPool() {
      if (requestInFlight) {
        return;
      }

      requestInFlight = true;

      try {
        const entries = await getWaitingPool();
        const namedRows = await addPatientNames(entries, patientNameCache);

        if (disposed) {
          return;
        }

        setRows(namedRows);
        setErrorMessage(null);
        setLoadState('loaded');
        hasLoaded = true;
      } catch (error) {
        if (disposed) {
          return;
        }

        setErrorMessage(waitingPoolErrorMessage(error));
        if (!hasLoaded) {
          setLoadState('error');
        }
      } finally {
        requestInFlight = false;
      }
    }

    const initialLoadId = window.setTimeout(() => void refreshWaitingPool(), 0);
    const pollId = window.setInterval(() => void refreshWaitingPool(), POLL_INTERVAL_MS);

    return () => {
      disposed = true;
      window.clearTimeout(initialLoadId);
      window.clearInterval(pollId);
    };
  }, []);

  return (
    <DashboardShell sectionLabel="Doctor Dashboard">
      <div className="mt-6 flex flex-wrap gap-3">
        <Link
          to="/patients/search"
          className="inline-block border-2 border-slate-400 px-4 py-3 text-xs font-bold uppercase tracking-[0.12em] text-slate-700 hover:border-brand-blue hover:text-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
        >
          Search Patients
        </Link>
      </div>

      <section className="mt-8" aria-labelledby="waiting-pool-heading">
        <div className="flex flex-wrap items-end justify-between gap-4 border-b border-slate-300 pb-3">
          <div>
            <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">
              Shared Queue
            </p>
            <h2 id="waiting-pool-heading" className="mt-1 text-xl font-semibold text-slate-900">
              Waiting Pool
            </h2>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <p className="text-xs text-slate-500">Refreshes every 5 seconds</p>
            <button
              type="button"
              disabled
              title="Call Next Patient will be available in the next workflow"
              className="border-2 border-slate-300 bg-slate-100 px-4 py-2 text-xs font-bold uppercase tracking-[0.12em] text-slate-400 disabled:cursor-not-allowed"
            >
              Call Next Patient
            </button>
          </div>
        </div>

        <div aria-live="polite" className="mt-6">
          {errorMessage && (
            <div className="mb-4 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3" role="alert">
              <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">
                Waiting Pool Refresh Failed
              </p>
              <p className="mt-1 text-sm text-red-900">{errorMessage}</p>
            </div>
          )}

          {loadState === 'loading' && (
            <p className="text-sm text-slate-500">Loading shared waiting pool…</p>
          )}

          {loadState === 'loaded' && rows.length === 0 && (
            <div className="border-t-4 border-b border-slate-400 bg-slate-50 px-6 py-3">
              <p className="text-sm text-slate-700">No patients currently waiting</p>
            </div>
          )}

          {loadState === 'loaded' && rows.length > 0 && (
            <div className="overflow-x-auto border border-slate-300">
              <table className="min-w-full divide-y divide-slate-200 text-sm">
                <thead className="bg-slate-50">
                  <tr>
                    {['Queue Number', 'Patient', 'Check-in Time'].map((heading) => (
                      <th
                        key={heading}
                        scope="col"
                        className="whitespace-nowrap px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600"
                      >
                        {heading}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-200">
                  {rows.map((row) => (
                    <tr key={row.queueId}>
                      <td className="whitespace-nowrap px-4 py-3 font-semibold text-slate-900">
                        {row.queueNumber}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3">
                        {row.patientName === PATIENT_UNAVAILABLE ? (
                          <span className="text-slate-500">{row.patientName}</span>
                        ) : (
                          <Link
                            to={`/patients/${row.patientId}`}
                            className="text-brand-blue hover:text-brand-blue-dark hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
                          >
                            {row.patientName}
                          </Link>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-700">
                        {formatCheckInTime(row.checkedInAt)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </section>
    </DashboardShell>
  );
}
