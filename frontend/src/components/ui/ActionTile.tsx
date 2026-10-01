import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Icon, type IconName } from './Icon';

interface ActionTileProps {
  to: string;
  title: string;
  description: string;
  icon: IconName;
}

// A dashboard shortcut. The whole tile is the link, so the target is large and the
// description says what will happen before the user commits to it.
export function ActionTile({ to, title, description, icon }: ActionTileProps) {
  return (
    <Link
      to={to}
      className="group flex items-center gap-4 rounded-lg border border-slate-200 bg-white p-5 transition-colors hover:border-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue/40 focus-visible:ring-offset-2"
    >
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-md bg-brand-blue-tint text-brand-blue">
        <Icon name={icon} />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-semibold text-slate-900">{title}</span>
        <span className="mt-0.5 block truncate text-sm text-slate-500">{description}</span>
      </span>
      <Icon
        name="chevronRight"
        className="h-4 w-4 text-slate-300 transition-colors group-hover:text-brand-blue"
      />
    </Link>
  );
}

export function ActionTileGrid({ label, children }: { label: string; children: ReactNode }) {
  return (
    <nav aria-label={label} className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {children}
    </nav>
  );
}
