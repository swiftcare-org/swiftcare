import type { ReactNode } from 'react';

export type StatusBadgeTone = 'success' | 'warning' | 'info' | 'danger' | 'neutral';

const TONES: Record<StatusBadgeTone, string> = {
  success: 'border-emerald-700 bg-emerald-50 text-emerald-900',
  warning: 'border-amber-600 bg-amber-50 text-amber-900',
  info: 'border-brand-blue bg-brand-blue-tint text-brand-blue-dark',
  danger: 'border-red-700 bg-red-50 text-red-900',
  neutral: 'border-slate-400 bg-slate-100 text-slate-700',
};

interface StatusBadgeProps {
  tone: StatusBadgeTone;
  className?: string;
  children: ReactNode;
}

export function StatusBadge({ tone, className = '', children }: StatusBadgeProps) {
  return (
    <span
      className={`inline-flex items-center whitespace-nowrap border px-2 py-0.5 text-xs font-bold uppercase tracking-[0.08em] ${TONES[tone]} ${className}`}
    >
      {children}
    </span>
  );
}
