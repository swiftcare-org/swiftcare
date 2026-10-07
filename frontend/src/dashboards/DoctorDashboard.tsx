import { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getPatient } from '../api/patients';
import {
  callNextPatient,
  getWaitingPool,
  type TodayQueueEntry,
} from '../api/queue';
import { useAuth } from '../auth/useAuth';
import {
  loadCurrentPatient,
  PATIENT_UNAVAILABLE,
  resolveCurrentPatient,
  type CurrentPatient,
} from '../consultations/currentPatient';
import {
  findPendingPrescriptionContext,
  type PrescriptionContext,
} from '../prescriptions/pendingPrescription';
import { Banner } from '../components/ui/Banner';
import { Button, ButtonLink } from '../components/ui/Button';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { SectionCard } from '../components/ui/SectionCard';
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
import { formatTime } from '../lib/format';
import { DashboardShell } from './DashboardShell';

type WaitingPoolLoadState = 'loading' | 'loaded' | 'error';
type CurrentPatientLoadState = 'loading' | 'loaded' | 'error';
type CallNextState = 'idle' | 'calling';

interface WaitingPoolRow extends TodayQueueEntry {
  patientName: string;
}

const POLL_INTERVAL_MS = 5_000;
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

export function DoctorDashboard() {
  const { user } = useAuth();
  // A one-off confirmation handed over by the page the doctor just came from.
  const location = useLocation();
  const navigate = useNavigate();
  const [arrivalNotice] = useState<string | null>(() => {
    const notice = (location.state as { notice?: unknown } | null)?.notice;
    return typeof notice === 'string' ? notice : null;
  });

  // Shown once: clearing it from the history entry keeps a reload from repeating it.
  useEffect(() => {
    if (arrivalNotice) {
      void navigate('.', { replace: true, state: null });
    }
  }, [arrivalNotice, navigate]);
  const [loadState, setLoadState] = useState<WaitingPoolLoadState>('loading');
  const [rows, setRows] = useState<WaitingPoolRow[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [callNextState, setCallNextState] = useState<CallNextState>('idle');
  const [callNextError, setCallNextError] = useState<string | null>(null);
  const [currentPatient, setCurrentPatient] = useState<CurrentPatient | null>(null);
  const [currentPatientLoadState, setCurrentPatientLoadState] = useState<CurrentPatientLoadState>('loading');
  const [currentPatientError, setCurrentPatientError] = useState<string | null>(null);
  const [currentRefreshKey, setCurrentRefreshKey] = useState(0);
  const [callNextBlocked, setCallNextBlocked] = useState(false);
  const [pendingPrescription, setPendingPrescription] =
    useState<PrescriptionContext | null>(null);
  const assignmentVersion = useRef(0);

  useEffect(() => {
    let disposed = false;

    async function loadPendingPrescription() {
      try {
        const context = await findPendingPrescriptionContext();
        if (!disposed) {
          setPendingPrescription(context);
        }
      } catch {
        if (!disposed) {
          setPendingPrescription(null);
        }
      }
    }

    void loadPendingPrescription();

    return () => {
      disposed = true;
    };
  }, [currentPatient?.queueId, user?.userId]);

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

  useEffect(() => {
    let disposed = false;
    let requestInFlight = false;

    async function refreshCurrentPatient() {
      if (requestInFlight) {
        return;
      }

      requestInFlight = true;
      const requestedVersion = assignmentVersion.current;

      try {
        const assignment = await loadCurrentPatient();
        if (disposed || requestedVersion !== assignmentVersion.current) {
          return;
        }

        setCurrentPatient(assignment);
        setCurrentPatientLoadState('loaded');
        setCurrentPatientError(null);
        setCallNextBlocked(false);
        if (assignment) {
          setCallNextError(null);
        }
      } catch {
        if (disposed || requestedVersion !== assignmentVersion.current) {
          return;
        }

        setCurrentPatientLoadState('error');
        setCurrentPatientError('Unable to load your current consultation. Please try again.');
      } finally {
        requestInFlight = false;
      }
    }

    const initialLoadId = window.setTimeout(() => void refreshCurrentPatient(), 0);
    const pollId = window.setInterval(() => void refreshCurrentPatient(), POLL_INTERVAL_MS);

    return () => {
      disposed = true;
      window.clearTimeout(initialLoadId);
      window.clearInterval(pollId);
    };
  }, [user?.userId, currentRefreshKey]);

  const callNextDisabled =
    loadState !== 'loaded' ||
    currentPatientLoadState !== 'loaded' ||
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
    assignmentVersion.current += 1;

    try {
      const calledPatient = await callNextPatient();
      const waitingRow = rows.find((row) => row.patientId === calledPatient.patientId);
      const assignedPatient = await resolveCurrentPatient(
        calledPatient,
        waitingRow?.patientName,
      );

      assignmentVersion.current += 1;
      setCurrentPatient(assignedPatient);
      setCurrentPatientLoadState('loaded');
      setCurrentPatientError(null);
      setRows((currentRows) =>
        currentRows.filter((row) => row.queueId !== calledPatient.queueId),
      );
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        setRows([]);
        return;
      }

      if (error instanceof ApiError && error.status === 409) {
        assignmentVersion.current += 1;
        setCallNextBlocked(true);
        setCurrentPatientLoadState('loading');
        setCurrentRefreshKey((key) => key + 1);
      }

      setCallNextError(callNextErrorMessage(error));
    } finally {
      setCallNextState('idle');
    }
  }

  // Constraint made visible: when the button is off, say why and what unlocks it.
  const callNextDisabledReason =
    currentPatient !== null
      ? 'Complete the current consultation before calling the next patient.'
      : loadState === 'loaded' && currentPatientLoadState === 'loaded' && rows.length === 0
        ? 'There is no one to call until a patient checks in.'
        : null;

  return (
    <DashboardShell sectionLabel="Doctor Dashboard">
      {arrivalNotice && (
        <Banner tone="success" role="status">
          <span data-testid="dashboard-notice">{arrivalNotice}</span>
        </Banner>
      )}

      {pendingPrescription && (
        <Banner tone="warning" title="Prescription Pending">
          <p>
            Finish the prescription for{' '}
            {pendingPrescription.patientName ?? 'your latest completed consultation'}.
          </p>
          <ButtonLink to="/doctor/prescription" state={pendingPrescription} size="sm" className="mt-3">
            Continue Prescription
          </ButtonLink>
        </Banner>
      )}

      <SectionCard
        title="Waiting Pool"
        titleId="waiting-pool-heading"
        description="Shared queue. Refreshes every 5 seconds."
        actions={
          <Button
            disabled={callNextDisabled && callNextState !== 'calling'}
            loading={callNextState === 'calling'}
            onClick={() => void handleCallNext()}
            aria-describedby={callNextDisabledReason ? 'call-next-reason' : undefined}
          >
            {callNextState === 'calling' ? 'Calling…' : 'Call Next Patient'}
          </Button>
        }
      >
        <div aria-live="polite" className="space-y-4">
          {callNextDisabledReason && (
            <p id="call-next-reason" className="text-xs text-slate-500">
              {callNextDisabledReason}
            </p>
          )}

          {currentPatientLoadState === 'loading' && !currentPatient && (
            <LoadingText>Loading your current consultation…</LoadingText>
          )}

          {currentPatientError && (
            <Banner tone="error" title="Current Consultation Unavailable" role="alert">
              {currentPatientError}
            </Banner>
          )}

          {currentPatient && (
            <Banner tone="info" title="Current Consultation">
              <p className="break-words text-base font-semibold text-slate-900">
                Currently with you:{' '}
                <Link to={`/patients/${currentPatient.patientId}`} className={textLinkClassName}>
                  {currentPatient.queueNumber}
                </Link>{' '}
                <Link to={`/patients/${currentPatient.patientId}`} className={textLinkClassName}>
                  {currentPatient.patientName}
                </Link>
              </p>
              <p className="mt-1 text-xs text-slate-600">Room {currentPatient.roomNumber}</p>
              <ButtonLink to="/doctor/consultation" size="sm" className="mt-3">
                Record Consultation
              </ButtonLink>
            </Banner>
          )}

          {callNextError && (
            <Banner tone="warning" title="Unable to Call Patient" role="alert">
              {callNextError}
            </Banner>
          )}

          {errorMessage && (
            <Banner tone="error" title="Waiting Pool Refresh Failed" role="alert">
              {errorMessage}
            </Banner>
          )}

          {loadState === 'loading' && <LoadingText>Loading shared waiting pool…</LoadingText>}

          {loadState === 'loaded' && rows.length === 0 && (
            <EmptyState>
              <p>No patients currently waiting</p>
            </EmptyState>
          )}

          {loadState === 'loaded' && rows.length > 0 && (
            <div className={tableWrapperClassName}>
              <table className={tableClassName}>
                <thead className={tableHeadClassName}>
                  <tr>
                    {['Queue Number', 'Patient', 'Check-in Time'].map((heading) => (
                      <th key={heading} scope="col" className={tableHeaderCellClassName}>
                        {heading}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className={tableBodyClassName}>
                  {rows.map((row) => (
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
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </SectionCard>
    </DashboardShell>
  );
}
