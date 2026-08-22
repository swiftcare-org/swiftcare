import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';
import { logout } from '../api/auth';
import swiftcareLogo from '../assets/swiftcare-logo.svg';

interface DashboardShellProps {
  sectionLabel: string;
  children?: ReactNode;
}

export function DashboardShell({ sectionLabel, children }: DashboardShellProps) {
  const { user, signOut } = useAuth();
  const navigate = useNavigate();

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
        <div className="mx-auto flex max-w-4xl items-center justify-between px-6 py-4">
          <img src={swiftcareLogo} alt="SwiftCare" className="h-6 w-auto" />
          <button
            type="button"
            onClick={handleSignOut}
            className="border-2 border-slate-400 px-4 py-3 text-xs font-bold uppercase tracking-[0.12em] text-slate-700 hover:border-brand-blue hover:text-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
          >
            Sign Out
          </button>
        </div>
        <div className="bg-brand-blue px-6 py-2">
          <h1 className="mx-auto max-w-4xl text-xs font-bold uppercase tracking-[0.22em] text-white">{sectionLabel}</h1>
        </div>
      </header>

      <main className="mx-auto max-w-4xl px-6 py-10">
        <div className="border border-slate-300 bg-white px-6 py-8 shadow-[4px_4px_0_rgba(15,23,42,0.06)]">
          <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">Signed in as</p>
          <p className="mt-1 text-2xl font-semibold text-slate-900">{user?.fullName}</p>
          <p className="mt-1 text-sm text-slate-500">
            {user?.role}
            {user?.roomNumber ? ` — Room ${user.roomNumber}` : ''}
          </p>
          {children}
        </div>
      </main>
    </div>
  );
}
