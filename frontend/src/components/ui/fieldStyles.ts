export const eyebrowClassName = 'text-xs font-bold uppercase tracking-[0.12em] text-slate-500';

export const labelClassName ='block text-xs font-bold uppercase tracking-[0.12em] text-slate-600';

export function inputClassName(hasError = false): string {
  return `mt-1.5 block w-full border-2 bg-white px-3 py-2.5 text-sm text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 read-only:bg-slate-100 read-only:text-slate-600 disabled:bg-slate-100 disabled:text-slate-500 ${
    hasError ? 'border-red-600' : 'border-slate-400 focus:border-brand-blue'
  }`;
}
