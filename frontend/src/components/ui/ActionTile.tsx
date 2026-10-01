import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

interface ActionTileProps {
  to: string;
  title: string;
  description: string;
}

// A dashboard destination. The whole tile is the link, so the target is large and the
// description says what will happen before the user commits to it.
export function ActionTile({ to, title, description }: ActionTileProps) {
  return (
    <Link
      to={to}
      className="group flex items-start justify-between gap-3 border-2 border-slate-300 bg-white px-4 py-4 hover:border-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
    >
      <span className="min-w-0">
        <span className="block text-sm font-bold uppercase tracking-[0.12em] text-slate-900 group-hover:text-brand-blue">
          {title}
        </span>
        <span className="mt-1 block text-sm text-slate-600">{description}</span>
      </span>
      <span className="text-lg font-bold leading-none text-brand-blue" aria-hidden="true">
        →
      </span>
    </Link>
  );
}

export function ActionTileGrid({ label, children }: { label: string; children: ReactNode }) {
  return (
    <nav aria-label={label} className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {children}
    </nav>
  );
}
