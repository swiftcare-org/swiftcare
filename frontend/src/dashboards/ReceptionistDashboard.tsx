import { useEffect, useState } from 'react';
import { getTodayQueue, type TodayQueueEntry, type TodayQueueStatus } from '../api/queue';
import { Banner } from '../components/ui/Banner';
import { ButtonLink } from '../components/ui/Button';
import { SectionCard } from '../components/ui/SectionCard';
import { StatCard, StatGrid } from '../components/ui/StatCard';
import { DashboardShell } from './DashboardShell';

type LoadState = 'loading' | 'loaded' | 'error';

const POLL_INTERVAL_MS = 15_000;
const PLACEHOLDER = '-';

export function ReceptionistDashboard() {
  const [entries, setEntries] = useState<TodayQueueEntry[]>([]);
  const [loadState, setLoadState] = useState<LoadState>('loading');

  useEffect(() => {
    let disposed = false;
    let hasLoaded = false;

    async function refresh() {
      try {
        const loaded = await getTodayQueue();
        if (!disposed) {
          setEntries(loaded);
          setLoadState('loaded');
          hasLoaded = true;
        }
      } catch {
        // A failed refresh keeps the last good figures on screen.
        if (!disposed && !hasLoaded) {
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

  const count = (status?: TodayQueueStatus) =>
    loadState === 'loaded'
      ? entries.filter((entry) => !status || entry.status === status).length
      : PLACEHOLDER;

  return (
    <DashboardShell sectionLabel="Receptionist Dashboard">
      {loadState === 'error' && (
        <Banner tone="error" title="Queue Figures Unavailable" role="alert">
          Unable to load today&apos;s queue. Please refresh the page.
        </Banner>
      )}

      <StatGrid label="Today's queue">
        <StatCard label="Checked in today" value={count()} />
        <StatCard label="Waiting" value={count('WAITING')} />
        <StatCard label="In consultation" value={count('IN_CONSULTATION')} />
        <StatCard label="Completed" value={count('COMPLETED')} />
      </StatGrid>

      <SectionCard
        title="Next Patient"
        description="Register someone new, or find an existing patient to check them in."
        actions={
          <>
            <ButtonLink to="/patients/search" variant="secondary">
              Find Patient
            </ButtonLink>
            <ButtonLink to="/reception/patients/new">Register Patient</ButtonLink>
          </>
        }
      />
    </DashboardShell>
  );
}
