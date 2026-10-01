// The shared visual vocabulary. Pages import these instead of writing their own classes,
// so a change of look is made here once.
export const focusRingClassName =
  'focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue/40 focus-visible:ring-offset-2';

export const eyebrowClassName = 'text-xs font-medium text-slate-500';

export const labelClassName = 'block text-sm font-medium text-slate-700';

export const subHeadingClassName = 'text-base font-semibold text-slate-900';

export const legendClassName = 'px-1.5 text-sm font-semibold text-slate-700';

export const detailTermClassName = 'text-xs font-medium text-slate-500';

/** A bordered item inside a card: a list entry, a fieldset, a nested form. */
export const itemCardClassName = 'rounded-lg border border-slate-200';

export const dividerClassName = 'border-t border-slate-200';

export function inputClassName(hasError = false): string {
  return `mt-1.5 block w-full rounded-lg border bg-white px-3 py-2 text-sm text-slate-900 shadow-sm placeholder:text-slate-400 focus:outline-none focus:ring-2 read-only:bg-slate-50 read-only:text-slate-600 disabled:bg-slate-50 disabled:text-slate-500 disabled:shadow-none ${
    hasError
      ? 'border-red-500 focus:border-red-500 focus:ring-red-500/20'
      : 'border-slate-300 focus:border-brand-blue focus:ring-brand-blue/20'
  }`;
}
