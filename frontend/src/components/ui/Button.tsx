import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Link, type LinkProps } from 'react-router-dom';

export type ButtonVariant = 'primary' | 'secondary' | 'danger';
export type ButtonSize = 'md' | 'sm';

const BASE =
  'inline-flex items-center justify-center gap-2 rounded-md border text-center font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2';

const SIZES: Record<ButtonSize, string> = {
  md: 'px-4 py-2 text-sm',
  sm: 'px-3 py-1.5 text-sm',
};

const ENABLED: Record<ButtonVariant, string> = {
  primary:
    'border-brand-blue bg-brand-blue text-white hover:border-brand-blue-dark hover:bg-brand-blue-dark focus-visible:ring-brand-blue/40',
  secondary:
    'border-slate-300 bg-white text-slate-700 hover:bg-slate-50 hover:text-slate-900 focus-visible:ring-brand-blue/40',
  danger:
    'border-red-600 bg-red-600 text-white hover:border-red-700 hover:bg-red-700 focus-visible:ring-red-600/40',
};

const DISABLED: Record<ButtonVariant, string> = {
  primary: 'cursor-not-allowed border-slate-200 bg-slate-200 text-slate-500',
  secondary: 'cursor-not-allowed border-slate-200 bg-slate-50 text-slate-400',
  danger: 'cursor-not-allowed border-slate-200 bg-slate-200 text-slate-500',
};

// A busy button keeps its colour so the user can see which action is running.
const BUSY: Record<ButtonVariant, string> = {
  primary: 'cursor-progress border-brand-blue-dark bg-brand-blue-dark text-white',
  secondary: 'cursor-progress border-slate-300 bg-slate-50 text-slate-700',
  danger: 'cursor-progress border-red-700 bg-red-700 text-white',
};

interface StyleOptions {
  variant?: ButtonVariant;
  size?: ButtonSize;
  fullWidth?: boolean;
  className?: string;
}

function buttonClassName(
  { variant = 'primary', size = 'md', fullWidth = false, className = '' }: StyleOptions,
  state: 'enabled' | 'disabled' | 'busy' = 'enabled',
): string {
  const tone = state === 'busy' ? BUSY[variant] : state === 'disabled' ? DISABLED[variant] : ENABLED[variant];
  return [BASE, SIZES[size], tone, fullWidth ? 'w-full' : '', className].filter(Boolean).join(' ');
}

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement>, StyleOptions {
  loading?: boolean;
  children: ReactNode;
}

export function Button({
  variant,
  size,
  fullWidth,
  className,
  loading = false,
  disabled = false,
  type = 'button',
  children,
  ...rest
}: ButtonProps) {
  const state = loading ? 'busy' : disabled ? 'disabled' : 'enabled';

  return (
    <button
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={buttonClassName({ variant, size, fullWidth, className }, state)}
      {...rest}
    >
      {loading && (
        <span
          className="inline-block h-4 w-4 shrink-0 animate-spin rounded-full border-2 border-current border-r-transparent"
          aria-hidden="true"
        />
      )}
      {children}
    </button>
  );
}

interface ButtonLinkProps extends LinkProps, StyleOptions {
  children: ReactNode;
}

export function ButtonLink({ variant, size, fullWidth, className, children, ...rest }: ButtonLinkProps) {
  return (
    <Link className={buttonClassName({ variant, size, fullWidth, className })} {...rest}>
      {children}
    </Link>
  );
}
