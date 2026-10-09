import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Link, NavLink, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';
import type { UserRole } from '../auth/types';
import { logout } from '../api/auth';
import { BackLink } from '../components/ui/BackLink';
import { Icon, type IconName } from '../components/ui/Icon';
import swiftcareLogo from '../assets/swiftcare-logo.png';

interface DashboardShellProps {
  /** The page's name: shown as the page heading and in the browser tab. */
  sectionLabel: string;
  /** Where "back" goes from this page. Omit on pages reachable from the sidebar. */
  backLink?: { to: string; destination: string };
  children?: ReactNode;
}

interface NavItem {
  to: string;
  label: string;
  icon: IconName;
}

// One list per role, so every signed-in screen offers the same way to move around.
const NAVIGATION: Record<UserRole, NavItem[]> = {
  Doctor: [
    { to: '/doctor', label: 'Dashboard', icon: 'home' },
    { to: '/patients/search', label: 'Patients', icon: 'search' },
  ],
  Receptionist: [
    { to: '/reception', label: 'Dashboard', icon: 'home' },
    { to: '/reception/queue', label: 'Queue', icon: 'queue' },
    { to: '/activity', label: 'Activity Feed', icon: 'activity' },
    { to: '/patients/search', label: 'Patients', icon: 'search' },
    { to: '/reception/patients/new', label: 'Register Patient', icon: 'userPlus' },
  ],
  Admin: [
    { to: '/admin', label: 'Dashboard', icon: 'home' },
    { to: '/admin/users', label: 'Staff Accounts', icon: 'users' },
    { to: '/admin/audit-log', label: 'Audit Log', icon: 'clipboard' },
    { to: '/admin/reports/daily', label: 'Daily Report', icon: 'chart' },
    { to: '/admin/reports/monthly', label: 'Monthly Report', icon: 'calendar' },
    { to: '/activity', label: 'Activity Feed', icon: 'activity' },
    { to: '/patients/search', label: 'Patients', icon: 'search' },
  ],
};

const NAV_ITEM_BASE =
  'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue/40';

function initialsOf(fullName: string): string {
  const parts = fullName.trim().split(/\s+/);
  const first = parts[0]?.[0] ?? '';
  const last = parts.length > 1 ? parts[parts.length - 1][0] : '';
  return (first + last).toUpperCase();
}

export function DashboardShell({ sectionLabel, backLink, children }: DashboardShellProps) {
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [menuOpenAt, setMenuOpenAt] = useState<string | null>(null);
  const menuButton = useRef<HTMLButtonElement>(null);
  const menuPanel = useRef<HTMLDivElement>(null);

  // The phone menu is open only for the page it was opened on, so following a link closes it.
  const menuOpen = menuOpenAt === location.pathname;

  useEffect(() => {
    document.title = `${sectionLabel} · SwiftCare`;
  }, [sectionLabel]);

  // Opening the phone menu moves focus into it, so keyboard users land on the links.
  useEffect(() => {
    if (menuOpen) {
      menuPanel.current?.querySelector<HTMLElement>('a, button')?.focus();
    }
  }, [menuOpen]);

  // Escape closes the phone menu; Tab cycles inside it instead of reaching the page behind.
  function handleMenuKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      setMenuOpenAt(null);
      menuButton.current?.focus();
      return;
    }

    if (event.key !== 'Tab' || !menuPanel.current) {
      return;
    }

    const focusable = menuPanel.current.querySelectorAll<HTMLElement>('a, button');
    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  function handleSignOut() {
    // logout() is fired before the token is cleared, since it needs the still-present
    // bearer token to authenticate the audit-log call. It's deliberately not awaited:
    // ending the local session must never depend on the network or on AuthService being
    // reachable, so a rejected request here is swallowed and the audit row is simply lost.
    logout().catch(() => {});
    signOut();
    navigate('/login', { replace: true });
  }

  const homeRoute = user ? roleRoutes[user.role] : '/login';
  const items = user ? NAVIGATION[user.role] : [];

  const navigation = (
    <nav aria-label="Main" className="flex-1 space-y-1 overflow-y-auto px-3 py-4">
      {items.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          end
          className={({ isActive }) =>
            `${NAV_ITEM_BASE} ${
              isActive
                ? 'bg-brand-blue-tint text-brand-blue-dark'
                : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900'
            }`
          }
        >
          <Icon name={item.icon} />
          {item.label}
        </NavLink>
      ))}
      <a
        href="/queue/display"
        target="_blank"
        rel="noreferrer"
        className={`${NAV_ITEM_BASE} text-slate-600 hover:bg-slate-100 hover:text-slate-900`}
      >
        <Icon name="display" />
        Waiting Room Display
      </a>
    </nav>
  );

  const account = user && (
    <div className="border-t border-slate-200 p-3">
      <div className="flex items-center gap-3 px-1 py-1">
        <span
          className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-slate-100 text-xs font-semibold text-slate-600"
          aria-hidden="true"
        >
          {initialsOf(user.fullName)}
        </span>
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-slate-900">{user.fullName}</p>
          <p className="truncate text-xs text-slate-500">
            {user.role}
            {user.roomNumber ? ` · Room ${user.roomNumber}` : ''}
          </p>
        </div>
      </div>
      <button
        type="button"
        onClick={handleSignOut}
        className={`${NAV_ITEM_BASE} mt-2 w-full text-slate-600 hover:bg-slate-100 hover:text-slate-900`}
      >
        <Icon name="signOut" />
        Sign Out
      </button>
    </div>
  );

  const logo = (
    <Link
      to={homeRoute}
      aria-label="SwiftCare, go to your dashboard"
      className="inline-flex rounded focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue/40 focus-visible:ring-offset-4"
    >
      <img src={swiftcareLogo} alt="" width={603} height={176} className="h-9 w-auto" />
    </Link>
  );

  return (
    <div className="min-h-screen bg-slate-50">
      {/* Desktop: navigation is always on screen. */}
      <aside className="fixed inset-y-0 left-0 z-20 hidden w-64 flex-col border-r border-slate-200 bg-white lg:flex">
        <div className="flex h-16 items-center border-b border-slate-200 px-5">{logo}</div>
        {navigation}
        {account}
      </aside>

      {/* Phone and tablet: a top bar, with the same navigation behind a menu button. */}
      <header className="sticky top-0 z-20 flex h-14 items-center justify-between border-b border-slate-200 bg-white px-4 lg:hidden">
        {logo}
        <button
          ref={menuButton}
          type="button"
          onClick={() => setMenuOpenAt(menuOpen ? null : location.pathname)}
          aria-expanded={menuOpen}
          aria-controls="mobile-navigation"
          className="inline-flex items-center gap-2 rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue/40"
        >
          <Icon name={menuOpen ? 'close' : 'menu'} />
          Menu
        </button>
      </header>

      {menuOpen && (
        <div
          id="mobile-navigation"
          ref={menuPanel}
          role="dialog"
          aria-label="Menu"
          onKeyDown={handleMenuKeyDown}
          className="fixed inset-x-0 bottom-0 top-14 z-10 flex flex-col bg-white lg:hidden"
        >
          {navigation}
          {account}
        </div>
      )}

      <div className="lg:pl-64">
        <main className="mx-auto max-w-6xl space-y-6 px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          <div>
            {backLink && (
              <nav aria-label="Breadcrumb" className="mb-3">
                <BackLink to={backLink.to} destination={backLink.destination} />
              </nav>
            )}
            <h1 className="text-xl font-semibold tracking-tight text-slate-900 sm:text-2xl">{sectionLabel}</h1>
          </div>
          {children}
        </main>
      </div>
    </div>
  );
}
