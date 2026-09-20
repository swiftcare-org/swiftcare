import { Link, useLocation } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';

interface PrescriptionContext {
  completed?: boolean;
  patientName?: string;
  queueNumber?: string;
}

export function PrescriptionPlaceholderPage() {
  const location = useLocation();
  const context = location.state as PrescriptionContext | null;

  return (
    <DashboardShell sectionLabel="Prescription">
      <section className="mt-6 border-t-4 border-b border-brand-blue bg-blue-50 px-6 py-5">
        <h1 className="text-xl font-semibold text-slate-900">Prescription</h1>
        {context?.completed && (
          <p className="mt-2 text-sm font-semibold text-emerald-800">
            Consultation completed successfully.
          </p>
        )}
        {context?.completed && context.queueNumber && (
          <p className="mt-2 text-sm text-slate-700">
            {context.queueNumber} {context.patientName}
          </p>
        )}
        <p className="mt-3 text-sm text-slate-700">
          Prescription entry is not available yet. This page will be connected to the
          prescription workflow when that feature is implemented.
        </p>
        <Link
          to="/doctor"
          className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
        >
          Back to Doctor Dashboard
        </Link>
      </section>
    </DashboardShell>
  );
}
