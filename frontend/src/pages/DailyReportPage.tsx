import { useState } from 'react';
import { DashboardShell } from '../dashboards/DashboardShell';
import {
  getDailyActivityReport,
  getDailyPrescriptionReport,
  type DailyActivityReport,
} from '../api/reports';
import { Banner } from '../components/ui/Banner';
import { CountTable } from '../components/ui/CountTable';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { Field } from '../components/ui/Field';
import { SectionCard } from '../components/ui/SectionCard';
import { StatCard, StatGrid } from '../components/ui/StatCard';
import { clinicTodayForDateInput, formatDate } from '../lib/format';
import { useReportSections } from '../lib/useReportSections';

function hasNoActivity(report: DailyActivityReport): boolean {
  return (
    report.totalPatients === 0 &&
    report.topDiagnoses.length === 0 &&
    report.patientsPerRoom.every((room) => room.patients === 0)
  );
}

export function DailyReportPage() {
  const [today] = useState(clinicTodayForDateInput);
  const [date, setDate] = useState(today);
  const {
    loading,
    activity: activityReport,
    prescriptions: prescriptionReport,
  } = useReportSections(date, getDailyActivityReport, getDailyPrescriptionReport);

  const nothingRecorded =
    activityReport !== null &&
    prescriptionReport !== null &&
    hasNoActivity(activityReport) &&
    prescriptionReport.totalWritten === 0;

  return (
    <DashboardShell sectionLabel="Daily Report">
      <SectionCard title="Report Date" description="Figures cover one clinic day, midnight to midnight.">
        <Field id="report-date" label="Date" required className="max-w-xs">
          {(control) => (
            <input
              {...control}
              type="date"
              value={date}
              max={today}
              onChange={(event) => setDate(event.target.value)}
            />
          )}
        </Field>
      </SectionCard>

      {!date && <EmptyState>Select a date to see its report.</EmptyState>}
      {loading && <LoadingText>Loading the report…</LoadingText>}

      {!loading && nothingRecorded && (
        <EmptyState>No activity recorded for this date.</EmptyState>
      )}

      {!loading && date && !nothingRecorded && (
        <>
          {!activityReport && (
            <Banner tone="warning" title="Activity data currently unavailable." role="status">
              Patient, room and diagnosis figures could not be loaded. Prescription figures are not affected.
            </Banner>
          )}

          {activityReport && (
            <>
              <StatGrid label={`Patients on ${formatDate(date)}`}>
                <StatCard label="Total patients" value={activityReport.totalPatients} />
                <StatCard label="New patients" value={activityReport.newPatients} />
                <StatCard label="Returning patients" value={activityReport.returningPatients} />
              </StatGrid>

              <SectionCard title="Patients per Room" description="Patients called to each room.">
                <CountTable
                  labelHeading="Room"
                  countHeading="Patients"
                  testId="daily-report-rooms"
                  rows={activityReport.patientsPerRoom.map((room) => ({
                    key: room.roomNumber,
                    label: `Room ${room.roomNumber}`,
                    count: room.patients,
                  }))}
                />
              </SectionCard>

              <SectionCard title="Top Diagnoses" description="The five most common diagnoses of completed consultations.">
                {activityReport.topDiagnoses.length === 0 ? (
                  <EmptyState>No diagnoses recorded for this date.</EmptyState>
                ) : (
                  <CountTable
                    labelHeading="Diagnosis"
                    countHeading="Consultations"
                    testId="daily-report-diagnoses"
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
            <StatGrid label={`Prescriptions on ${formatDate(date)}`}>
              <StatCard label="Prescriptions written" value={prescriptionReport.totalWritten} />
              <StatCard label="Dispensed" value={prescriptionReport.totalDispensed} />
              <StatCard label="Pending" value={prescriptionReport.totalPending} />
            </StatGrid>
          )}
        </>
      )}
    </DashboardShell>
  );
}
