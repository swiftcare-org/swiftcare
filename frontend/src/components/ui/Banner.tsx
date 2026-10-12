import type { ReactNode } from 'react';

export type BannerTone = 'success' | 'error' | 'warning' | 'info' | 'neutral';

const TONES: Record<BannerTone, { box: string; title: string; body: string }> = {
  success: { box: 'border-emerald-200 bg-emerald-50', title: 'text-emerald-900', body: 'text-emerald-800' },
  error: { box: 'border-red-200 bg-red-50', title: 'text-red-900', body: 'text-red-800' },
  warning: { box: 'border-amber-200 bg-amber-50', title: 'text-amber-900', body: 'text-amber-800' },
  info: { box: 'border-blue-200 bg-brand-blue-tint', title: 'text-slate-900', body: 'text-slate-700' },
  neutral: { box: 'border-slate-200 bg-slate-50', title: 'text-slate-900', body: 'text-slate-700' },
};

interface BannerProps {
  tone: BannerTone;
  title?: string;
  /** Use "alert" only for problems the user must act on; live regions announce the rest. */
  role?: 'alert' | 'status';
  className?: string;
  children?: ReactNode;
}

export function Banner({ tone, title, role, className = '', children }: BannerProps) {
  const style = TONES[tone];

  return (
    <div role={role} className={`rounded-md border px-4 py-3 ${style.box} ${className}`}>
      {title && <p className={`text-sm font-semibold ${style.title}`}>{title}</p>}
      {children && <div className={`text-sm ${title ? 'mt-0.5' : ''} ${style.body}`}>{children}</div>}
    </div>
  );
}
