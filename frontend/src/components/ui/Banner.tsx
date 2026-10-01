import type { ReactNode } from 'react';

export type BannerTone = 'success' | 'error' | 'warning' | 'info' | 'neutral';

const TONES: Record<BannerTone, { box: string; title: string; body: string }> = {
  success: { box: 'border-emerald-700 bg-emerald-50', title: 'text-emerald-800', body: 'text-emerald-900' },
  error: { box: 'border-red-700 bg-red-50', title: 'text-red-800', body: 'text-red-900' },
  warning: { box: 'border-amber-600 bg-amber-50', title: 'text-amber-800', body: 'text-amber-900' },
  info: { box: 'border-brand-blue bg-brand-blue-tint', title: 'text-brand-blue-dark', body: 'text-slate-900' },
  neutral: { box: 'border-slate-400 bg-slate-50', title: 'text-slate-600', body: 'text-slate-700' },
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
    <div role={role} className={`border-t-4 border-b px-4 py-3 sm:px-6 ${style.box} ${className}`}>
      {title && <p className={`text-[11px] font-bold uppercase tracking-[0.15em] ${style.title}`}>{title}</p>}
      {children && <div className={`text-sm ${title ? 'mt-1' : ''} ${style.body}`}>{children}</div>}
    </div>
  );
}
