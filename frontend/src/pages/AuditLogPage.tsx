import { useEffect, useState } from 'react';
import { DashboardShell } from '../dashboards/DashboardShell';
import { listAuditLog, type AuditLogEntry } from '../api/auditLogs';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { numericClassName } from '../components/ui/fieldStyles';
import { SectionCard } from '../components/ui/SectionCard';
import {
  tableBodyClassName,
  tableCellClassName,
  tableClassName,
  tableHeadClassName,
  tableHeaderCellClassName,
  tableKeyCellClassName,
  tableWrapperClassName,
} from '../components/ui/table';
import { formatDateTime } from '../lib/format';

type LoadState = 'loading' | 'loaded' | 'error';

const COLUMNS = ['Time', 'User', 'Action', 'Account', 'IP Address'];

// The server sends a stable code per action; the wording lives here with the rest of the copy.
const ACTION_LABELS: Record<string, string> = {
  LoginSucceeded: 'Signed in',
  LoginFailed: 'Sign-in failed',
  LoginBlocked: 'Sign-in blocked, account deactivated',
  Logout: 'Signed out',
  UserCreated: 'Created account',
  UserUpdated: 'Edited account',
  PasswordReset: 'Reset password',
  UserDeactivated: 'Deactivated account',
  UserReactivated: 'Reactivated account',
};

export function AuditLogPage() {
  const [entries, setEntries] = useState<AuditLogEntry[]>([]);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [refreshing, setRefreshing] = useState(false);

  const [reloadCount, setReloadCount] = useState(0);

  useEffect(() => {
    let disposed = false;

    async function load() {
      try {
        const loaded = await listAuditLog();
        if (!disposed) {
          setEntries(loaded);
          setLoadState('loaded');
        }
      } catch {
        if (!disposed) {
          setLoadState('error');
        }
      } finally {
        if (!disposed) {
          setRefreshing(false);
        }
      }
    }

    void load();

    return () => {
      disposed = true;
    };
  }, [reloadCount]);

  function handleRefresh() {
    setRefreshing(true);
    setReloadCount((count) => count + 1);
  }

  const summary =
    loadState === 'loaded' && entries.length > 0
      ? `${entries.length} most recent ${entries.length === 1 ? 'event' : 'events'}, newest first.`
      : undefined;

  return (
    <DashboardShell sectionLabel="Audit Log">
      <SectionCard
        title="Sign-in and Account Activity"
        description={summary}
        actions={
          <Button variant="secondary" size="sm" loading={refreshing} onClick={handleRefresh}>
            {refreshing ? 'Refreshing…' : 'Refresh'}
          </Button>
        }
      >
        {loadState === 'loading' && <LoadingText>Loading the audit log…</LoadingText>}
        {loadState === 'error' && (
          <Banner tone="error" title="Audit Log Unavailable" role="alert">
            Unable to load the audit log. Please try again.
          </Banner>
        )}
        {loadState === 'loaded' && entries.length === 0 && <EmptyState>No activity recorded yet.</EmptyState>}

        {loadState === 'loaded' && entries.length > 0 && (
          <div className={tableWrapperClassName}>
            <table className={tableClassName} data-testid="audit-log-table">
              <thead className={tableHeadClassName}>
                <tr>
                  {COLUMNS.map((heading) => (
                    <th key={heading} scope="col" className={tableHeaderCellClassName}>
                      {heading}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className={tableBodyClassName}>
                {entries.map((entry) => (
                  <tr key={entry.id} data-testid="audit-log-row">
                    <td className={`${tableCellClassName} whitespace-nowrap ${numericClassName}`}>
                      {formatDateTime(entry.occurredAt)}
                    </td>
                    <td className={tableKeyCellClassName}>{entry.username}</td>
                    <td className={tableCellClassName}>{ACTION_LABELS[entry.action] ?? entry.action}</td>
                    <td className={tableCellClassName}>{entry.targetUsername ?? '-'}</td>
                    <td className={`${tableCellClassName} ${numericClassName}`}>{entry.ipAddress}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>
    </DashboardShell>
  );
}
