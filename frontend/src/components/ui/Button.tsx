import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Link, type LinkProps } from 'react-router-dom';

export type ButtonVariant = 'primary' | 'secondary' | 'danger';
export type ButtonSize = 'md' | 'sm';

const BASE =
  'relative inline-flex items-center justify-center overflow-hidden border-2 text-center font-bold uppercase tracking-[0.12em] focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2';

const SIZES: Record<ButtonSize, string> = {
  md: 'px-4 py-3 text-sm',
  sm: 'px-3 py-2 text-xs',
};

const ENABLED: Record<ButtonVariant, string> = {
  primary:
    'border-brand-blue bg-brand-blue text-white hover:border-brand-blue-dark hover:bg-brand-blue-dark focus-visible:ring-brand-blue',
  secondary:
    'border-slate-400 bg-white text-slate-700 hover:border-brand-blue hover:text-brand-blue focus-visible:ring-brand-blue',
  danger:
    'border-red-700 bg-red-700 text-white hover:border-red-800 hover:bg-red-800 focus-visible:ring-red-700',
};

const DISABLED: Record<ButtonVariant, string> = {
  primary: 'cursor-not-allowed border-slate-300 bg-slate-300 text-slate-600',
  secondary: 'cursor-not-allowed border-slate-300 bg-slate-50 text-slate-400',
  danger: 'cursor-not-allowed border-slate-300 bg-slate-300 text-slate-600',
};

// A busy button keeps its colour so the user can see which action is running.
const BUSY: Record<ButtonVariant, string> = {
  primary: 'cursor-progress border-slate-900 bg-slate-900 text-white',
  secondary: 'cursor-progress border-slate-400 bg-slate-100 text-slate-700',
  danger: 'cursor-progress border-red-900 bg-red-900 text-white',
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
      {children}
      {loading && (
        <span className="absolute inset-x-0 bottom-0 block h-0.5 overflow-hidden bg-white/20" aria-hidden="true">
          <span className="block h-full w-1/3 animate-[loading-sweep_1.1s_ease-in-out_infinite] bg-current" />
        </span>
      )}
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
