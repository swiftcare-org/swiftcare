import type { ReactNode } from 'react';
import { inputClassName, labelClassName } from './fieldStyles';

export function RequiredMark() {
  return (
    <>
      <span className="ml-0.5 text-red-600" aria-hidden="true">
        *
      </span>
      <span className="sr-only"> (required)</span>
    </>
  );
}

export function OptionalMark() {
  return <span className="ml-1 font-normal text-slate-400">(optional)</span>;
}

export function RequiredLegend() {
  return (
    <p className="text-xs text-slate-500">
      Fields marked <span className="font-semibold text-red-600">*</span> are required.
    </p>
  );
}

export interface FieldControlProps {
  id: string;
  className: string;
  'aria-invalid': true | undefined;
  'aria-required': true | undefined;
  'aria-describedby': string | undefined;
}

interface FieldProps {
  id: string;
  label: ReactNode;
  required?: boolean;
  optional?: boolean;
  /** Shown before any error, so the rule is visible up front. */
  hint?: ReactNode;
  error?: string | null;
  /** A non-blocking caution, such as an out-of-range reading. */
  warning?: string | null;
  className?: string;
  children: (control: FieldControlProps) => ReactNode;
}

export function Field({ id, label, required, optional, hint, error, warning, className, children }: FieldProps) {
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const warningId = !error && warning ? `${id}-warning` : undefined;
  const describedBy = [hintId, errorId, warningId].filter(Boolean).join(' ') || undefined;

  return (
    <div className={className}>
      <label htmlFor={id} className={labelClassName}>
        {label}
        {required && <RequiredMark />}
        {optional && <OptionalMark />}
      </label>
      {children({
        id,
        className: inputClassName(Boolean(error)),
        'aria-invalid': error ? true : undefined,
        'aria-required': required ? true : undefined,
        'aria-describedby': describedBy,
      })}
      {hint && (
        <p id={hintId} className="mt-1 text-xs text-slate-500">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="mt-1 text-xs font-medium text-red-600">
          {error}
        </p>
      )}
      {!error && warning && (
        <p id={warningId} className="mt-1 text-xs font-medium text-amber-700">
          {warning}
        </p>
      )}
    </div>
  );
}
