import { useEffect, type ReactNode } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';
import { logout } from '../api/auth';
import { BackLink } from '../components/ui/BackLink';
import { Button } from '../components/ui/Button';
import swiftcareLogo from '../assets/swiftcare-logo.png';

interface DashboardShellProps {
  /** The page's name: shown as the page heading and in the browser tab. */
  sectionLabel: string;
  /** "wide" gives table-heavy pages the room to avoid sideways scrolling. */
  width?: 'default' | 'wide';
  /** Where "back" goes from this page. Omit on the dashboards themselves. */
  backLink?: { to: string; destination: string };
  children?: ReactNode;
}

const WIDTHS = {
  default: 'max-w-4xl',
  wide: 'max-w-6xl',
};

export function DashboardShell({ sectionLabel, width = 'default', backLink, children }: DashboardShellProps) {
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const column = `mx-auto ${WIDTHS[width]} px-4 sm:px-6`;

  useEffect(() => {
    document.title = `${sectionLabel} · SwiftCare`;
  }, [sectionLabel]);

  function handleSignOut() {
    // logout() is fired before the token is cleared, since it needs the still-present
    // bearer token to authenticate the audit-log call. It's deliberately not awaited:
    // ending the local session must never depend on the network or on AuthService being
    // reachable, so a rejected request here is swallowed and the audit row is simply lost.
    logout().catch(() => {});
    signOut();
    navigate('/login', { replace: true });
  }

  return (
    <div className="min-h-screen bg-slate-100">
      <header className="border-b border-slate-300 bg-white">
        <div className={`${column} flex items-center justify-between gap-3 py-3`}>
          <Link
            to={user ? roleRoutes[user.role] : '/login'}
            aria-label="SwiftCare, go to your dashboard"
            className="shrink-0 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-4"
          >
            <img src={swiftcareLogo} alt="" width={603} height={176} className="h-8 w-auto sm:h-9" />
          </Link>
          <div className="flex min-w-0 items-center gap-3 sm:gap-4">
            {user && (
              <div className="min-w-0 text-right">
                <p className="truncate text-sm font-semibold text-slate-900">{user.fullName}</p>
                <p className="truncate text-xs text-slate-500">
                  {user.role}
                  {user.roomNumber ? ` · Room ${user.roomNumber}` : ''}
                </p>
              </div>
            )}
            <Button variant="secondary" size="sm" className="shrink-0" onClick={handleSignOut}>
              Sign Out
            </Button>
          </div>
        </div>
        <div className="bg-slate-900 py-2.5">
          <h1 className={`${column} text-sm font-bold uppercase tracking-[0.18em] text-white`}>{sectionLabel}</h1>
        </div>
      </header>

      <main className={`${column} space-y-6 py-6 sm:py-8`}>
        {backLink && (
          <nav aria-label="Breadcrumb">
            <BackLink to={backLink.to} destination={backLink.destination} />
          </nav>
        )}
        {children}
      </main>
    </div>
  );
}
