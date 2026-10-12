// One formatter per shape of value, so a date or time reads the same on every page.
// Instants are shown in the clinic's time zone rather than the browser's, so two
// people looking at the same record always see the same time.
export const CLINIC_TIME_ZONE = 'Asia/Colombo';

const PLACEHOLDER = '-';
const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

const dateParts = new Intl.DateTimeFormat('en-US', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  timeZone: CLINIC_TIME_ZONE,
});

// A date-only value is a calendar day, not an instant, so it must not be shifted by
// a time zone. Formatting it as UTC noon in UTC keeps the day exactly as stored.
const calendarDateParts = new Intl.DateTimeFormat('en-US', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});

const timeFormatter = new Intl.DateTimeFormat('en-US', {
  hour: 'numeric',
  minute: '2-digit',
  hour12: true,
  timeZone: CLINIC_TIME_ZONE,
});

function partsOf(formatter: Intl.DateTimeFormat, date: Date): Record<string, string> {
  return Object.fromEntries(formatter.formatToParts(date).map(({ type, value }) => [type, value]));
}

function parse(value: string | null | undefined): { date: Date; calendarDay: boolean } | null {
  if (!value) {
    return null;
  }

  const match = DATE_ONLY_PATTERN.exec(value);
  const date = match
    ? new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]), 12))
    : new Date(value);

  return Number.isNaN(date.getTime()) ? null : { date, calendarDay: match !== null };
}

/** "01 Sep 2026" */
export function formatDate(value: string | null | undefined): string {
  const parsed = parse(value);
  if (!parsed) {
    return PLACEHOLDER;
  }

  const parts = partsOf(parsed.calendarDay ? calendarDateParts : dateParts, parsed.date);
  return `${parts.day} ${parts.month} ${parts.year}`;
}

/** "2:05 PM" */
export function formatTime(value: string | null | undefined): string {
  const parsed = parse(value);
  return parsed ? timeFormatter.format(parsed.date) : PLACEHOLDER;
}

/** "01 Sep 2026, 2:05 PM" */
export function formatDateTime(value: string | null | undefined): string {
  return parse(value) ? `${formatDate(value)}, ${formatTime(value)}` : PLACEHOLDER;
}

function plural(count: number, unit: string): string {
  return `${count} ${unit}${count === 1 ? '' : 's'}`;
}

/** "3 days", "3 weeks", "2 months": days under two weeks, weeks under eight, months after. */
export function formatElapsedDays(days: number): string {
  if (days < 14) {
    return plural(Math.max(days, 1), 'day');
  }

  if (days < 56) {
    return plural(Math.floor(days / 7), 'week');
  }

  return plural(Math.floor(days / 30), 'month');
}

/** Today's date in the clinic's time zone, as "yyyy-MM-dd" for a date input. */
export function clinicTodayForDateInput(): string {
  const parts = partsOf(
    new Intl.DateTimeFormat('en-US', {
      timeZone: CLINIC_TIME_ZONE,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }),
    new Date(),
  );

  return `${parts.year}-${parts.month}-${parts.day}`;
}
