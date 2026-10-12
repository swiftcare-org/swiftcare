import type { ReactNode } from 'react';

interface StatCardProps {
  label: string;
  /** The figure, or a placeholder while it loads. */
  value: ReactNode;
  hint?: string;
}

// One headline figure on a dashboard.
export function StatCard({ label, value, hint }: StatCardProps) {
  return (
    <div className="rounded-lg border border-slate-200 bg-white px-5 py-4">
      <dt className="text-sm font-medium text-slate-500">{label}</dt>
      <dd className="mt-1 text-3xl font-semibold tracking-tight text-slate-900">{value}</dd>
      {hint && <p className="mt-1 text-xs text-slate-500">{hint}</p>}
    </div>
  );
}

export function StatGrid({ label, children }: { label: string; children: ReactNode }) {
  return (
    <dl aria-label={label} className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {children}
    </dl>
  );
}
