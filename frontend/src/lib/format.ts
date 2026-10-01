// One formatter per shape of value, so a date reads the same on every page.
const LOCALE = 'en-GB';

const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

// A date-only string ("2026-10-01") is a calendar day, not an instant. Parsing it with
// new Date() treats it as UTC midnight, which can shift the day in other time zones.
function toLocalDate(value: string): Date {
  const match = DATE_ONLY_PATTERN.exec(value);
  return match ? new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3])) : new Date(value);
}

function isValid(date: Date): boolean {
  return !Number.isNaN(date.getTime());
}

export function formatDate(value: string | null | undefined): string {
  if (!value) return '';
  const date = toLocalDate(value);
  return isValid(date) ? date.toLocaleDateString(LOCALE, { day: '2-digit', month: 'short', year: 'numeric' }) : value;
}

export function formatTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  return isValid(date) ? date.toLocaleTimeString(LOCALE, { hour: '2-digit', minute: '2-digit' }) : value;
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  return isValid(date) ? `${formatDate(value)}, ${formatTime(value)}` : value;
}

export function formatMonthYear(value: string | null | undefined): string {
  if (!value) return '';
  const date = toLocalDate(value);
  return isValid(date) ? date.toLocaleDateString(LOCALE, { month: 'short', year: 'numeric' }) : value;
}
