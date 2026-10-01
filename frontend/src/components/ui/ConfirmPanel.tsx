import type { ReactNode } from 'react';
import { Button } from './Button';

interface ConfirmPanelProps {
  /** Id of the element that names the dialog, for assistive technology. */
  labelId: string;
  title: string;
  children: ReactNode;
  confirmLabel: string;
  busyLabel?: string;
  cancelLabel?: string;
  tone?: 'danger' | 'primary';
  busy?: boolean;
  error?: string | null;
  onConfirm: () => void;
  onCancel: () => void;
  className?: string;
}

// An inline second step for actions that cannot be undone. It sits beside the thing it
// affects rather than in a modal, so the user keeps the context they are deciding on.
export function ConfirmPanel({
  labelId,
  title,
  children,
  confirmLabel,
  busyLabel,
  cancelLabel = 'Cancel',
  tone = 'danger',
  busy = false,
  error,
  onConfirm,
  onCancel,
  className = '',
}: ConfirmPanelProps) {
  return (
    <div
      role="alertdialog"
      aria-labelledby={labelId}
      className={`border-t-4 border-b border-amber-600 bg-amber-50 px-4 py-3 ${className}`}
    >
      <p id={labelId} className="text-[11px] font-bold uppercase tracking-[0.15em] text-amber-800">
        {title}
      </p>
      <div className="mt-1 break-words text-sm text-amber-900" data-testid="confirm-message">
        {children}
      </div>
      {error && (
        <p role="alert" className="mt-2 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
          {error}
        </p>
      )}
      <div className="mt-3 flex flex-wrap gap-2">
        <Button variant={tone} size="sm" loading={busy} onClick={onConfirm}>
          {busy && busyLabel ? busyLabel : confirmLabel}
        </Button>
        <Button variant="secondary" size="sm" disabled={busy} onClick={onCancel}>
          {cancelLabel}
        </Button>
      </div>
    </div>
  );
}
