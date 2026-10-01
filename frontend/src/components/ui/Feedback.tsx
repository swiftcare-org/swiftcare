import type { ReactNode } from 'react';

interface LoadingTextProps {
  children: ReactNode;
  className?: string;
}

export function LoadingText({ children, className = '' }: LoadingTextProps) {
  return <p className={`text-sm text-slate-500 ${className}`}>{children}</p>;
}

interface EmptyStateProps {
  children: ReactNode;
  className?: string;
}

export function EmptyState({ children, className = '' }: EmptyStateProps) {
  return (
    <div
      className={`rounded-lg border border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500 ${className}`}
    >
      {children}
    </div>
  );
}
