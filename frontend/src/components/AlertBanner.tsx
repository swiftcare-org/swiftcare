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
      className={`rounded-lg border-l-4 px-4 py-3 ${toneClassNames[tone]}`}
      role="alert"
    >
      <p className="sr-only">{label}</p>
      <p className="break-words text-sm font-semibold">{children}</p>
    </div>
  );
}
