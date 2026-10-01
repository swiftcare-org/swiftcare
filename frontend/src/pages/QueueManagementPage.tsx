import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getPatient } from '../api/patients';
import {
  getPendingPrescriptions,
  getPrescriptionByQueueId,
  type Prescription,
} from '../api/prescriptions';
import { getTodayQueue, type TodayQueueEntry, type TodayQueueStatus } from '../api/queue';
import { Banner } from '../components/ui/Banner';
import { ButtonLink } from '../components/ui/Button';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { SectionCard } from '../components/ui/SectionCard';
import { StatusBadge, type StatusBadgeTone } from '../components/ui/StatusBadge';
import {
  tableBodyClassName,
  tableCellClassName,
  tableClassName,
  tableHeadClassName,
  tableHeaderCellClassName,
  tableKeyCellClassName,
  tableWrapperClassName,
  textLinkClassName,
} from '../components/ui/table';
import { DashboardShell } from '../dashboards/DashboardShell';
import { formatDateTime, formatTime } from '../lib/format';

type QueueLoadState = 'loading' | 'loaded' | 'error';
type PrescriptionDisplayState = Prescription['status'] | 'NOT_CREATED' | 'UNAVAILABLE';

interface QueueDisplayRow extends TodayQueueEntry {
  patientName: string;
  prescriptionState: PrescriptionDisplayState | null;
}

interface PendingPrescriptionRow {
  prescription: Prescription;
  queueEntry: QueueDisplayRow;
}

interface StatusPresentation {
  label: string;
  tone: StatusBadgeTone;
}

const POLL_INTERVAL_MS = 5_000;
const PATIENT_UNAVAILABLE = 'Patient unavailable';

const STATUS_PRESENTATIONS: Record<TodayQueueStatus, StatusPresentation> = {
  WAITING: { label: 'WAITING', tone: 'warning' },
  IN_CONSULTATION: { label: 'IN CONSULTATION', tone: 'info' },
  COMPLETED: { label: 'COMPLETED', tone: 'success' },
};

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
    prescriptionState: null,
  }));
}

async function addPrescriptionStates(rows: QueueDisplayRow[]): Promise<QueueDisplayRow[]> {
  return Promise.all(
    rows.map(async (row) => {
      if (row.status !== 'COMPLETED') {
        return row;
      }

      try {
        const prescription = await getPrescriptionByQueueId(row.queueId);
        return { ...row, prescriptionState: prescription.status };
      } catch (error) {
        return {
          ...row,
          prescriptionState:
            error instanceof ApiError && error.status === 404 ? 'NOT_CREATED' : 'UNAVAILABLE',
        };
      }
    }),
  );
}

function selectTodayPendingPrescriptions(
  prescriptions: Prescription[],
  rows: QueueDisplayRow[],
): PendingPrescriptionRow[] {
  const completedRows = new Map(
    rows
      .filter((row) => row.status === 'COMPLETED')
      .map((row) => [row.queueId, row]),
  );

  return prescriptions.flatMap((prescription) => {
    const queueEntry = completedRows.get(prescription.queueId);
    return queueEntry ? [{ prescription, queueEntry }] : [];
  });
}

function prescriptionCell(row: QueueDisplayRow) {
  if (row.status !== 'COMPLETED') {
    return (
      <span className="text-slate-500" title="Available after the consultation is completed">
        <span aria-hidden="true">-</span>
        <span className="sr-only">Available after the consultation is completed</span>
      </span>
    );
  }

  if (row.prescriptionState === 'PENDING' || row.prescriptionState === 'NOT_CREATED') {
    return (
      <ButtonLink to={`/prescriptions/queue/${row.queueId}`} variant="secondary" size="sm">
        View Prescription
      </ButtonLink>
    );
  }

  if (row.prescriptionState === 'DISPENSED') {
    return (
      <Link
        to={`/prescriptions/queue/${row.queueId}`}
        title="View the dispensed prescription"
        className="inline-flex focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
      >
        <StatusBadge tone="success" className="underline underline-offset-2">
          DISPENSED
        </StatusBadge>
      </Link>
    );
  }

  return <span className="text-xs text-slate-500">Status unavailable</span>;
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
  const [pendingRows, setPendingRows] = useState<PendingPrescriptionRow[]>([]);
  const [pendingLoadFailed, setPendingLoadFailed] = useState(false);
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
        const pendingRequest = getPendingPrescriptions().then(
          (prescriptions) => ({ status: 'fulfilled' as const, prescriptions }),
          () => ({ status: 'rejected' as const }),
        );
        const displayRows = await addPrescriptionStates(namedRows);
        const pendingResult = await pendingRequest;

        if (disposed) {
          return;
        }

        setRows(displayRows);
        if (pendingResult.status === 'fulfilled') {
          setPendingRows(selectTodayPendingPrescriptions(pendingResult.prescriptions, displayRows));
          setPendingLoadFailed(false);
        } else {
          setPendingRows([]);
          setPendingLoadFailed(true);
        }
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
    <DashboardShell
      sectionLabel="Queue Management"
      width="wide"
    >
      <div aria-live="polite" className="space-y-6">
        {errorMessage && (
          <Banner tone="error" title="Queue Refresh Failed" role="alert">
            {errorMessage}
          </Banner>
        )}

        {loadState === 'loaded' && (
          <SectionCard
            title="Pending Prescriptions"
            titleId="pending-prescriptions-heading"
            description="Medicines counter. Oldest prescription first."
          >
            {pendingLoadFailed ? (
              <Banner tone="error" role="alert">
                Unable to load pending prescriptions. Please try again.
              </Banner>
            ) : pendingRows.length === 0 ? (
              <Banner tone="success">
                <p className="font-semibold">All prescriptions dispensed today</p>
              </Banner>
            ) : (
              <ul className="divide-y divide-slate-100 overflow-hidden rounded-md border border-slate-200 bg-white">
                {pendingRows.map(({ prescription, queueEntry }) => (
                  <li
                    key={prescription.id}
                    className="flex flex-wrap items-center justify-between gap-3 px-4 py-3"
                  >
                    <div className="min-w-0">
                      <p className="break-words font-semibold text-slate-900">
                        {queueEntry.queueNumber} · {queueEntry.patientName}
                      </p>
                      <p className="mt-1 text-xs text-slate-600">
                        Prescribed {formatDateTime(prescription.createdAt)} by {prescription.doctorName}
                      </p>
                    </div>
                    <ButtonLink to={`/prescriptions/queue/${queueEntry.queueId}`} variant="secondary" size="sm">
                      View Prescription
                    </ButtonLink>
                  </li>
                ))}
              </ul>
            )}
          </SectionCard>
        )}

        <SectionCard title="Full Patient Queue" description="Today. Refreshes every 5 seconds.">
          {loadState === 'loading' && <LoadingText>Loading today’s queue…</LoadingText>}

          {loadState === 'error' && (
            <EmptyState>
              <p>Today’s queue could not be loaded. It will be retried automatically.</p>
            </EmptyState>
          )}

          {loadState === 'loaded' && rows.length === 0 && (
            <EmptyState>
              <p>No patients in today's queue yet</p>
            </EmptyState>
          )}

          {loadState === 'loaded' && rows.length > 0 && (
            <div className={tableWrapperClassName}>
              <table className={tableClassName}>
                <thead className={tableHeadClassName}>
                  <tr>
                    {['Queue Number', 'Patient', 'Check-in Time', 'Status', 'Room', 'Doctor', 'Prescription'].map(
                      (heading) => (
                        <th key={heading} scope="col" className={tableHeaderCellClassName}>
                          {heading}
                        </th>
                      ),
                    )}
                  </tr>
                </thead>
                <tbody className={tableBodyClassName}>
                  {rows.map((row) => {
                    const status = STATUS_PRESENTATIONS[row.status];

                    return (
                      <tr key={row.queueId}>
                        <td className={`whitespace-nowrap ${tableKeyCellClassName}`}>{row.queueNumber}</td>
                        <td className={tableCellClassName}>
                          {row.patientName === PATIENT_UNAVAILABLE ? (
                            <span className="text-slate-500">{row.patientName}</span>
                          ) : (
                            <Link to={`/patients/${row.patientId}`} className={textLinkClassName}>
                              {row.patientName}
                            </Link>
                          )}
                        </td>
                        <td className={`whitespace-nowrap ${tableCellClassName}`}>
                          {formatTime(row.checkedInAt)}
                        </td>
                        <td className={`whitespace-nowrap ${tableCellClassName}`}>
                          <StatusBadge tone={status.tone}>{status.label}</StatusBadge>
                        </td>
                        <td className={`whitespace-nowrap ${tableCellClassName}`}>{row.roomNumber ?? '-'}</td>
                        <td className={tableCellClassName}>{row.doctorName ?? '-'}</td>
                        <td className={`whitespace-nowrap ${tableCellClassName}`}>{prescriptionCell(row)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </SectionCard>
      </div>
    </DashboardShell>
  );
}
