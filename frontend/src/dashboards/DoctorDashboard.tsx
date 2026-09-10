import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getPatient } from '../api/patients';
import {
  callNextPatient,
  getWaitingPool,
  type CalledPatient,
  type TodayQueueEntry,
} from '../api/queue';
import { useAuth } from '../auth/useAuth';
import { DashboardShell } from './DashboardShell';

type WaitingPoolLoadState = 'loading' | 'loaded' | 'error';
type CallNextState = 'idle' | 'calling';

interface WaitingPoolRow extends TodayQueueEntry {
  patientName: string;
}

interface CurrentPatient extends CalledPatient {
  patientName: string;
}

const POLL_INTERVAL_MS = 5_000;
const CLINIC_TIME_ZONE = 'Asia/Colombo';
const PATIENT_UNAVAILABLE = 'Patient unavailable';
const CURRENT_PATIENT_STORAGE_PREFIX = 'swiftcare.doctor.current-patient';

const checkInTimeFormatter = new Intl.DateTimeFormat('en-LK', {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  timeZone: CLINIC_TIME_ZONE,
});

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

function readStoredCurrentPatient(userId: string | undefined): CurrentPatient | null {
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
    // Browser storage can be unavailable; the dashboard can still load without restoration.
  }

  return null;
}

function storeCurrentPatient(userId: string | undefined, patient: CurrentPatient): void {
  if (!userId) {
    return;
  }

  try {
    sessionStorage.setItem(currentPatientStorageKey(userId), JSON.stringify(patient));
  } catch {
    // The successful call remains visible for this render even if browser storage is unavailable.
  }
}

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

function callNextErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401 || error.status === 403) {
      return 'You are not authorized to call the next patient.';
    }

    if (error.status === 409 || error.status === 503) {
      return error.message;
    }
  }

  return 'Unable to call the next patient. Please try again.';
}

async function resolveCalledPatientName(
  calledPatient: CalledPatient,
  waitingRows: WaitingPoolRow[],
): Promise<string> {
  const waitingRow = waitingRows.find((row) => row.patientId === calledPatient.patientId);
  if (waitingRow && waitingRow.patientName !== PATIENT_UNAVAILABLE) {
    return waitingRow.patientName;
  }

  try {
    return (await getPatient(calledPatient.patientId)).fullName;
  } catch {
    return PATIENT_UNAVAILABLE;
  }
}

export function DoctorDashboard() {
  const { user } = useAuth();
  const [loadState, setLoadState] = useState<WaitingPoolLoadState>('loading');
  const [rows, setRows] = useState<WaitingPoolRow[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [callNextState, setCallNextState] = useState<CallNextState>('idle');
  const [callNextError, setCallNextError] = useState<string | null>(null);
  const [currentPatient, setCurrentPatient] = useState<CurrentPatient | null>(() =>
    readStoredCurrentPatient(user?.userId),
  );
  const [callNextBlocked, setCallNextBlocked] = useState(false);

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

  const callNextDisabled =
    loadState !== 'loaded' ||
    rows.length === 0 ||
    callNextState === 'calling' ||
    currentPatient !== null ||
    callNextBlocked;

  async function handleCallNext() {
    if (callNextDisabled) {
      return;
    }

    setCallNextState('calling');
    setCallNextError(null);

    try {
      const calledPatient = await callNextPatient();
      const patientName = await resolveCalledPatientName(calledPatient, rows);
      const assignedPatient = { ...calledPatient, patientName };

      storeCurrentPatient(user?.userId, assignedPatient);
      setCurrentPatient(assignedPatient);
      setRows((currentRows) =>
        currentRows.filter((row) => row.queueId !== calledPatient.queueId),
      );
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        setRows([]);
        return;
      }

      if (error instanceof ApiError && error.status === 409) {
        setCallNextBlocked(true);
      }

      setCallNextError(callNextErrorMessage(error));
    } finally {
      setCallNextState('idle');
    }
  }

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
              disabled={callNextDisabled}
              onClick={() => void handleCallNext()}
              className="bg-brand-blue px-4 py-2 text-xs font-bold uppercase tracking-[0.12em] text-white hover:bg-brand-blue-dark focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {callNextState === 'calling' ? 'Calling…' : 'Call Next Patient'}
            </button>
          </div>
        </div>

        <div aria-live="polite" className="mt-6">
          {currentPatient && (
            <div className="mb-4 border-t-4 border-b border-brand-blue bg-blue-50 px-6 py-3">
              <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-brand-blue-dark">
                Current Consultation
              </p>
              <p className="mt-1 text-base font-semibold text-slate-900">
                {`Currently with you: ${currentPatient.queueNumber} ${currentPatient.patientName}`}
              </p>
              <p className="mt-1 text-xs text-slate-600">Room {currentPatient.roomNumber}</p>
            </div>
          )}

          {callNextError && (
            <div className="mb-4 border-t-4 border-b border-amber-600 bg-amber-50 px-6 py-3" role="alert">
              <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-amber-800">
                Unable to Call Patient
              </p>
              <p className="mt-1 text-sm text-amber-900">{callNextError}</p>
            </div>
          )}

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
