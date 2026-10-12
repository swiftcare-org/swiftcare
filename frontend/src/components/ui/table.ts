// One definition of table chrome. Wide tables scroll inside their own wrapper, so the
// page itself never scrolls sideways.
// "relative" keeps visually hidden text inside cells from widening the page.
export const tableWrapperClassName = 'relative overflow-x-auto rounded-md border border-slate-200';
export const tableClassName = 'min-w-full divide-y divide-slate-200 text-sm';
export const tableHeadClassName = 'bg-slate-50';
export const tableHeaderCellClassName =
  'whitespace-nowrap px-4 py-2.5 text-left text-[11px] font-semibold uppercase tracking-wider text-slate-500';
export const tableBodyClassName = 'divide-y divide-slate-100 bg-white [&>tr:hover]:bg-slate-50/60';
export const tableCellClassName = 'px-4 py-3 text-slate-700';
export const tableKeyCellClassName = 'px-4 py-3 font-medium text-slate-900';

export const textLinkClassName =
  'rounded font-medium text-brand-blue hover:text-brand-blue-dark hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue/40 focus-visible:ring-offset-2';
