import { useState } from 'react';
import { DashboardShell } from '../dashboards/DashboardShell';
import { getMonthlyActivityReport, getMonthlyPrescriptionReport } from '../api/reports';
import { Banner } from '../components/ui/Banner';
import { CountTable } from '../components/ui/CountTable';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { Field } from '../components/ui/Field';
import { SectionCard } from '../components/ui/SectionCard';
import { StatCard, StatGrid } from '../components/ui/StatCard';
import { clinicTodayForDateInput } from '../lib/format';
import { useReportSections } from '../lib/useReportSections';

const MONTH_NAMES = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

// How many past years the year list offers, counting the current one.
const YEARS_OFFERED = 6;

// The day ranges behind "Week 1" to "Week 4", shown so the split is not a guess.
const WEEK_RANGES: Record<number, string> = {
  1: 'days 1 to 7',
  2: 'days 8 to 14',
  3: 'days 15 to 21',
  4: 'day 22 to month end',
};

export function MonthlyReportPage() {
  // "yyyy-MM-dd" for today at the clinic: the newest month that can have any activity.
  const [today] = useState(clinicTodayForDateInput);
  const currentYear = Number(today.slice(0, 4));
  const currentMonth = Number(today.slice(5, 7));

  const [year, setYear] = useState(currentYear);
  const [monthNumber, setMonthNumber] = useState(currentMonth);

  const month = `${year}-${String(monthNumber).padStart(2, '0')}`;
  const monthLabel = `${MONTH_NAMES[monthNumber - 1]} ${year}`;

  const {
    loading,
    activity: activityReport,
    prescriptions: prescriptionReport,
  } = useReportSections(month, getMonthlyActivityReport, getMonthlyPrescriptionReport);

  function handleYearChange(nextYear: number) {
    setYear(nextYear);
    // Moving to the current year must not leave a month selected that has not started yet.
    if (nextYear === currentYear && monthNumber > currentMonth) {
      setMonthNumber(currentMonth);
    }
  }

  const nothingRecorded =
    activityReport !== null &&
    prescriptionReport !== null &&
    activityReport.totalPatients === 0 &&
    activityReport.topDiagnoses.length === 0 &&
    prescriptionReport.totalWritten === 0;

  return (
    <DashboardShell sectionLabel="Monthly Report">
      <SectionCard title="Report Month" description="Figures cover every clinic day of the month.">
        <div className="grid max-w-md gap-4 sm:grid-cols-2">
          <Field id="report-month" label="Month" required>
            {(control) => (
              <select
                {...control}
                value={monthNumber}
                onChange={(event) => setMonthNumber(Number(event.target.value))}
              >
                {MONTH_NAMES.map((name, index) => (
                  <option key={name} value={index + 1} disabled={year === currentYear && index + 1 > currentMonth}>
                    {name}
                  </option>
                ))}
              </select>
            )}
          </Field>
          <Field id="report-year" label="Year" required>
            {(control) => (
              <select {...control} value={year} onChange={(event) => handleYearChange(Number(event.target.value))}>
                {Array.from({ length: YEARS_OFFERED }, (_, index) => currentYear - index).map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            )}
          </Field>
        </div>
      </SectionCard>

      {loading && <LoadingText>Loading the report…</LoadingText>}

      {!loading && nothingRecorded && <EmptyState>No activity recorded for this month.</EmptyState>}

      {!loading && !nothingRecorded && (
        <>
          {!activityReport && (
            <Banner tone="warning" title="Activity data currently unavailable." role="status">
              Patient, weekly and diagnosis figures could not be loaded. Prescription figures are not affected.
            </Banner>
          )}

          {activityReport && (
            <>
              <StatGrid label={`Patients in ${monthLabel}`}>
                <StatCard label="Total patients" value={activityReport.totalPatients} />
                <StatCard label="New patients" value={activityReport.newPatients} />
                <StatCard label="Returning patients" value={activityReport.returningPatients} />
              </StatGrid>

              <SectionCard
                title="Weekly Breakdown"
                description="Each patient is counted in the week of their first visit that month."
              >
                <CountTable
                  labelHeading="Week"
                  countHeading="Patients"
                  testId="monthly-report-weeks"
                  rows={activityReport.weeklyBreakdown.map((week) => ({
                    key: String(week.week),
                    label: `Week ${week.week} (${WEEK_RANGES[week.week] ?? 'rest of the month'})`,
                    count: week.patients,
                  }))}
                />
              </SectionCard>

              <SectionCard
                title="Most Common Diagnoses"
                description="The five most common diagnoses of completed consultations."
              >
                {activityReport.topDiagnoses.length === 0 ? (
                  <EmptyState>No diagnoses recorded for this month.</EmptyState>
                ) : (
                  <CountTable
                    labelHeading="Diagnosis"
                    countHeading="Consultations"
                    testId="monthly-report-diagnoses"
                    rows={activityReport.topDiagnoses.map((diagnosis) => ({
                      key: diagnosis.diagnosis,
                      label: diagnosis.diagnosis,
                      count: diagnosis.count,
                    }))}
                  />
                )}
              </SectionCard>
            </>
          )}

          {!prescriptionReport && (
            <Banner tone="warning" title="Prescription data currently unavailable." role="status">
              Prescription figures could not be loaded. Activity figures are not affected.
            </Banner>
          )}

          {prescriptionReport && (
            <StatGrid label={`Prescriptions in ${monthLabel}`}>
              <StatCard label="Prescriptions written" value={prescriptionReport.totalWritten} />
              <StatCard label="Dispensed" value={prescriptionReport.totalDispensed} />
            </StatGrid>
          )}
        </>
      )}
    </DashboardShell>
  );
}
