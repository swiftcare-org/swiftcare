import { Link } from 'react-router-dom';
import { DashboardShell } from './DashboardShell';

export function AdminDashboard() {
  return (
    <DashboardShell sectionLabel="Admin Dashboard">
      <div className="mt-6 flex flex-wrap gap-3">
        <Link
          to="/admin/users"
          className="inline-block border-2 border-slate-400 px-4 py-3 text-xs font-bold uppercase tracking-[0.12em] text-slate-700 hover:border-brand-blue hover:text-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
        >
          Manage Users
        </Link>
        <Link
          to="/reception/patients/new"
          className="inline-block border-2 border-slate-400 px-4 py-3 text-xs font-bold uppercase tracking-[0.12em] text-slate-700 hover:border-brand-blue hover:text-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
        >
          Register Patient
        </Link>
      </div>
      <p className="mt-6 text-sm text-slate-500">Operational reports and staff oversight will be added in a later story.</p>
    </DashboardShell>
  );
}
