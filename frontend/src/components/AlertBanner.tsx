import type { ReactNode } from 'react';

export type AlertBannerTone = 'allergy' | 'condition' | 'followUp';

interface AlertBannerProps {
  tone: AlertBannerTone;
  label: string;
  children: ReactNode;
}

const toneClassNames: Record<AlertBannerTone, string> = {
  allergy: 'border-red-700 bg-red-50 text-red-900',
  condition: 'border-amber-600 bg-amber-50 text-amber-900',
  followUp: 'border-brand-blue bg-brand-blue-tint text-slate-900',
};

export function AlertBanner({ tone, label, children }: AlertBannerProps) {
  return (
    <div
      className={`border-t-4 border-b px-4 py-3 sm:px-6 ${toneClassNames[tone]}`}
      role="alert"
    >
      <p className="sr-only">{label}</p>
      <p className="text-sm font-bold">{children}</p>
    </div>
  );
}
