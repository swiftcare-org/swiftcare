import type { ReactNode } from 'react';

export type AlertBannerTone = 'allergy' | 'condition' | 'followUp';

interface AlertBannerProps {
  tone: AlertBannerTone;
  children: ReactNode;
}

const toneClassNames: Record<AlertBannerTone, string> = {
  allergy: 'border-red-700 bg-red-50 text-red-900',
  condition: 'border-amber-600 bg-amber-50 text-amber-900',
  followUp: 'border-blue-700 bg-blue-50 text-blue-900',
};

export function AlertBanner({ tone, children }: AlertBannerProps) {
  return (
    <div
      className={`border-t-4 border-b px-6 py-3 ${toneClassNames[tone]}`}
      role="alert"
    >
      <p className="text-sm font-bold">{children}</p>
    </div>
  );
}
