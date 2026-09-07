import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getPatient } from '../api/patients';
import { getTodayQueue, type TodayQueueEntry, type TodayQueueStatus } from '../api/queue';
import { DashboardShell } from '../dashboards/DashboardShell';

type QueueLoadState = 'loading' | 'loaded' | 'error';

interface QueueDisplayRow extends TodayQueueEntry {
  patientName: string;
}

interface StatusPresentation {
  label: string;
  className: string;
}

const POLL_INTERVAL_MS = 5_000;
const CLINIC_TIME_ZONE = 'Asia/Colombo';
const PATIENT_UNAVAILABLE = 'Patient unavailable';

const STATUS_PRESENTATIONS: Record<TodayQueueStatus, StatusPresentation> = {
  WAITING: {
    label: '⏳ WAITING',
    className: 'border-amber-300 bg-amber-50 text-amber-900',
  },
  IN_CONSULTATION: {
    label: '🔵 IN CONSULTATION',
    className: 'border-blue-300 bg-blue-50 text-blue-900',
  },
  COMPLETED: {
    label: '✅ COMPLETED',
    className: 'border-emerald-300 bg-emerald-50 text-emerald-900',
  },
};

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
): Promise<QueueDisplayRow[]> {
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

function queueErrorMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 403) {
    return 'You are not authorized to view today\'s queue.';
  }

  return "Unable to load today's queue. Please try again.";
}

export function QueueManagementPage() {
  const [loadState, setLoadState] = useState<QueueLoadState>('loading');
  const [rows, setRows] = useState<QueueDisplayRow[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let disposed = false;
    let requestInFlight = false;
    let hasLoaded = false;
    const patientNameCache = new Map<string, string>();

    async function refreshQueue() {
      if (requestInFlight) {
        return;
      }

      requestInFlight = true;

      try {
        const entries = await getTodayQueue();
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

        setErrorMessage(queueErrorMessage(error));
        if (!hasLoaded) {
          setLoadState('error');
        }
      } finally {
        requestInFlight = false;
      }
    }

    const initialLoadId = window.setTimeout(() => void refreshQueue(), 0);
    const pollId = window.setInterval(() => void refreshQueue(), POLL_INTERVAL_MS);

    return () => {
      disposed = true;
      window.clearTimeout(initialLoadId);
      window.clearInterval(pollId);
    };
  }, []);

  return (
    <DashboardShell sectionLabel="Queue Management">
      <Link
        to="/reception"
        className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
      >
        ← Back to Dashboard
      </Link>

      <div className="mt-6 flex items-end justify-between gap-4 border-b border-slate-300 pb-3">
        <div>
          <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">Today</p>
          <h2 className="mt-1 text-xl font-semibold text-slate-900">Full Patient Queue</h2>
        </div>
        <p className="text-xs text-slate-500">Refreshes every 5 seconds</p>
      </div>

      <div aria-live="polite" className="mt-6">
        {errorMessage && (
          <div className="mb-4 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3" role="alert">
            <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">
              Queue Refresh Failed
            </p>
            <p className="mt-1 text-sm text-red-900">{errorMessage}</p>
          </div>
        )}

        {loadState === 'loading' && <p className="text-sm text-slate-500">Loading today’s queue…</p>}

        {loadState === 'loaded' && rows.length === 0 && (
          <div className="border-t-4 border-b border-slate-400 bg-slate-50 px-6 py-3">
            <p className="text-sm text-slate-700">No patients in today's queue yet</p>
          </div>
        )}

        {loadState === 'loaded' && rows.length > 0 && (
          <div className="overflow-x-auto border border-slate-300">
            <table className="min-w-full divide-y divide-slate-200 text-sm">
              <thead className="bg-slate-50">
                <tr>
                  {['Queue Number', 'Patient', 'Check-in Time', 'Status', 'Room', 'Doctor', 'Prescription'].map(
                    (heading) => (
                      <th
                        key={heading}
                        scope="col"
                        className="whitespace-nowrap px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600"
                      >
                        {heading}
                      </th>
                    ),
                  )}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200">
                {rows.map((row) => {
                  const status = STATUS_PRESENTATIONS[row.status];

                  return (
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
                      <td className="whitespace-nowrap px-4 py-3">
                        <span
                          className={`inline-flex border px-2 py-1 text-xs font-bold ${status.className}`}
                        >
                          {status.label}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-700">
                        {row.roomNumber ?? '—'}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-700">
                        {row.doctorName ?? '—'}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                        <span title="Prescription status is not available yet">—</span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </DashboardShell>
  );
}
