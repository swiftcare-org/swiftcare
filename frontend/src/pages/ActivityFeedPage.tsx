import { useEffect, useRef, useState } from 'react';
import { DashboardShell } from '../dashboards/DashboardShell';
import { listActivity, type ActivityEntry, type ActivityType } from '../api/notifications';
import { getPatient } from '../api/patients';
import { Banner } from '../components/ui/Banner';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { numericClassName } from '../components/ui/fieldStyles';
import { SectionCard } from '../components/ui/SectionCard';
import { StatusBadge, type StatusBadgeTone } from '../components/ui/StatusBadge';
import { formatDateTime } from '../lib/format';

type LoadState = 'loading' | 'loaded' | 'error';

const POLL_INTERVAL_MS = 10_000;

const TYPE_BADGES: Record<ActivityType, { label: string; tone: StatusBadgeTone }> = {
  PatientCheckedIn: { label: 'Check-in', tone: 'info' },
  PatientCalled: { label: 'Called', tone: 'warning' },
  ConsultationCompleted: { label: 'Completed', tone: 'success' },
};

const UNKNOWN_BADGE: { label: string; tone: StatusBadgeTone } = { label: 'Event', tone: 'neutral' };

// The sentence for one event. The name is left out until PatientService has returned it.
function describe(entry: ActivityEntry, patientName: string | undefined): string {
  const named = patientName ? ` ${patientName}` : '';

  switch (entry.type) {
    case 'PatientCheckedIn':
      return entry.isNewPatient
        ? `New patient${named} registered and checked in.`
        : `Returning patient${named} checked in.`;
    case 'PatientCalled':
      return `${entry.queueNumber ?? 'Patient'}${named} called to Room ${entry.roomNumber ?? '-'} by ${entry.doctorName ?? 'the doctor'}.`;
    case 'ConsultationCompleted':
      return patientName ? `Consultation completed for ${patientName}.` : 'Consultation completed.';
    default:
      return 'Department event recorded.';
  }
}

export function ActivityFeedPage() {
  const [entries, setEntries] = useState<ActivityEntry[]>([]);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [refreshFailed, setRefreshFailed] = useState(false);
  const [patientNames, setPatientNames] = useState<Record<string, string>>({});
  // Patients already looked up or being looked up, so a poll never repeats a lookup.
  const requestedPatients = useRef(new Set<string>());

  useEffect(() => {
    let disposed = false;
    let hasLoaded = false;
    const requested = requestedPatients.current;

    // The feed carries patient IDs only; names come from PatientService, which owns them.
    async function resolveNames(loaded: ActivityEntry[]) {
      const missing = [...new Set(loaded.map((entry) => entry.patientId))].filter((id) => !requested.has(id));
      if (missing.length === 0) {
        return;
      }

      missing.forEach((id) => requested.add(id));
      const results = await Promise.allSettled(missing.map((id) => getPatient(id)));

      const resolved: Record<string, string> = {};
      results.forEach((result, index) => {
        if (result.status === 'fulfilled' && !disposed) {
          resolved[missing[index]] = result.value.fullName;
        } else {
          // A failed lookup is tried again on the next poll.
          requested.delete(missing[index]);
        }
      });

      if (!disposed && Object.keys(resolved).length > 0) {
        setPatientNames((current) => ({ ...current, ...resolved }));
      }
    }

    async function refresh() {
      try {
        const loaded = await listActivity();
        if (disposed) {
          return;
        }

        setEntries(loaded);
        setLoadState('loaded');
        setRefreshFailed(false);
        hasLoaded = true;
        void resolveNames(loaded);
      } catch {
        if (disposed) {
          return;
        }

        // A failed refresh keeps the last good feed on screen and says it may be stale.
        if (hasLoaded) {
          setRefreshFailed(true);
        } else {
          setLoadState('error');
        }
      }
    }

    const initialLoadId = window.setTimeout(() => void refresh(), 0);
    const pollId = window.setInterval(() => void refresh(), POLL_INTERVAL_MS);

    return () => {
      disposed = true;
      window.clearTimeout(initialLoadId);
      window.clearInterval(pollId);
    };
  }, []);

  return (
    <DashboardShell sectionLabel="Activity Feed">
      <SectionCard
        title="Department Activity"
        description="Check-ins, patients called and completed consultations, newest first. Updates every 10 seconds."
      >
        {loadState === 'loading' && <LoadingText>Loading the activity feed…</LoadingText>}
        {loadState === 'error' && (
          <Banner tone="error" title="Activity Feed Unavailable" role="alert">
            Unable to load the activity feed. It will be tried again automatically.
          </Banner>
        )}
        {loadState === 'loaded' && refreshFailed && (
          <Banner tone="warning" title="Feed Not Updating" role="status" className="mb-4">
            The latest activity could not be loaded. Showing the last feed received.
          </Banner>
        )}
        {loadState === 'loaded' && entries.length === 0 && <EmptyState>No activity recorded yet.</EmptyState>}

        {loadState === 'loaded' && entries.length > 0 && (
          <ol className="divide-y divide-slate-100" data-testid="activity-feed">
            {entries.map((entry) => {
              const badge = TYPE_BADGES[entry.type] ?? UNKNOWN_BADGE;

              return (
                <li
                  key={entry.id}
                  data-testid="activity-entry"
                  className="flex flex-col gap-1 py-3 first:pt-0 last:pb-0 sm:flex-row sm:items-baseline sm:gap-4"
                >
                  <time
                    dateTime={entry.occurredAt}
                    className={`shrink-0 text-xs text-slate-500 sm:w-40 ${numericClassName}`}
                  >
                    {formatDateTime(entry.occurredAt)}
                  </time>
                  <span className="shrink-0 sm:w-24">
                    <StatusBadge tone={badge.tone}>{badge.label}</StatusBadge>
                  </span>
                  <p className="min-w-0 break-words text-sm text-slate-900">
                    {describe(entry, patientNames[entry.patientId])}
                  </p>
                </li>
              );
            })}
          </ol>
        )}
      </SectionCard>
    </DashboardShell>
  );
}
