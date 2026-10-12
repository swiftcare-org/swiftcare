import { useEffect, useState } from 'react';
import { listUsers, type UserSummary } from '../api/users';
import { Banner } from '../components/ui/Banner';
import { ButtonLink } from '../components/ui/Button';
import { SectionCard } from '../components/ui/SectionCard';
import { StatCard, StatGrid } from '../components/ui/StatCard';
import { DashboardShell } from './DashboardShell';

type LoadState = 'loading' | 'loaded' | 'error';

const PLACEHOLDER = '-';

export function AdminDashboard() {
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [loadState, setLoadState] = useState<LoadState>('loading');

  useEffect(() => {
    let disposed = false;

    async function loadUsers() {
      try {
        const loaded = await listUsers();
        if (!disposed) {
          setUsers(loaded);
          setLoadState('loaded');
        }
      } catch {
        if (!disposed) {
          setLoadState('error');
        }
      }
    }

    void loadUsers();

    return () => {
      disposed = true;
    };
  }, []);

  const count = (predicate: (user: UserSummary) => boolean) =>
    loadState === 'loaded' ? users.filter(predicate).length : PLACEHOLDER;

  return (
    <DashboardShell sectionLabel="Admin Dashboard">
      {loadState === 'error' && (
        <Banner tone="error" title="Staff Figures Unavailable" role="alert">
          Unable to load the staff accounts. Please refresh the page.
        </Banner>
      )}

      <StatGrid label="Staff accounts">
        <StatCard label="Active accounts" value={count((user) => user.isActive)} />
        <StatCard label="Doctors" value={count((user) => user.role === 'Doctor')} />
        <StatCard label="Receptionists" value={count((user) => user.role === 'Receptionist')} />
        <StatCard label="Administrators" value={count((user) => user.role === 'Admin')} />
      </StatGrid>

      <SectionCard
        title="Staff Accounts"
        description="Create accounts, edit details, reset passwords and deactivate staff who leave."
        actions={<ButtonLink to="/admin/users">Open Staff Accounts</ButtonLink>}
      />

      <SectionCard
        title="Reports"
        description="Patients, top diagnoses and prescriptions for any date or month."
        actions={
          <>
            <ButtonLink to="/admin/reports/daily" variant="secondary">
              Open Daily Report
            </ButtonLink>
            <ButtonLink to="/admin/reports/monthly" variant="secondary">
              Open Monthly Report
            </ButtonLink>
          </>
        }
      />

      <SectionCard
        title="Audit Log"
        description="Review sign-ins, sign-outs and every change made to an account."
        actions={
          <ButtonLink to="/admin/audit-log" variant="secondary">
            Open Audit Log
          </ButtonLink>
        }
      />
    </DashboardShell>
  );
}
