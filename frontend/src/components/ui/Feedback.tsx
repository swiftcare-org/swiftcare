import type { ReactNode } from 'react';

interface LoadingTextProps {
  children: ReactNode;
  className?: string;
}

export function LoadingText({ children, className = '' }: LoadingTextProps) {
  return <p className={`text-sm text-slate-600 ${className}`}>{children}</p>;
}

interface EmptyStateProps {
  children: ReactNode;
  className?: string;
}

export function EmptyState({ children, className = '' }: EmptyStateProps) {
  return (
    <div className={`border border-dashed border-slate-300 bg-slate-50 px-4 py-4 text-sm text-slate-600 ${className}`}>
      {children}
    </div>
  );
}
