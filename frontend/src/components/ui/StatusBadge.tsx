import type { HTMLAttributes, ReactNode } from 'react';

export type StatusBadgeTone = 'success' | 'warning' | 'info' | 'danger' | 'neutral';

const TONES: Record<StatusBadgeTone, string> = {
  success: 'bg-emerald-50 text-emerald-800 ring-emerald-200',
  warning: 'bg-amber-50 text-amber-800 ring-amber-200',
  info: 'bg-brand-blue-tint text-brand-blue-dark ring-blue-200',
  danger: 'bg-red-50 text-red-800 ring-red-200',
  neutral: 'bg-slate-100 text-slate-700 ring-slate-200',
};

interface StatusBadgeProps extends HTMLAttributes<HTMLSpanElement> {
  tone: StatusBadgeTone;
  children: ReactNode;
}

export function StatusBadge({ tone, className = '', children, ...rest }: StatusBadgeProps) {
  return (
    <span
      className={`inline-flex items-center whitespace-nowrap rounded-full px-2.5 py-0.5 text-[11px] font-semibold uppercase tracking-wide ring-1 ring-inset ${TONES[tone]} ${className}`}
      {...rest}
    >
      {children}
    </span>
  );
}
