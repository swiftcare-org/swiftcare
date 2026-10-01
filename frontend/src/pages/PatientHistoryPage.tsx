import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';
import { ApiError } from '../api/client';
import { getPatient } from '../api/patients';
import type { PatientProfile } from '../api/patients';
import { getPatientConsultationHistory, getPatientVitalsHistory } from '../api/consultations';
import type { Consultation, VitalSigns } from '../api/consultations';
import { trendFor } from '../consultations/vitalsTrend';
import type { TrendMetric } from '../consultations/vitalsTrend';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';
import { Banner } from '../components/ui/Banner';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { SectionCard } from '../components/ui/SectionCard';
import {
  tableBodyClassName,
  tableCellClassName,
  tableClassName,
  tableHeadClassName,
  tableHeaderCellClassName,
  tableKeyCellClassName,
  tableWrapperClassName,
} from '../components/ui/table';
import { formatDate, formatDateTime } from '../lib/format';

type PatientLoadState = 'loading' | 'loaded' | 'notFound' | 'error';
type SectionLoadState = 'loading' | 'loaded' | 'error';

const NOTES_SUMMARY_LENGTH = 80;
const EMPTY_VALUE = '-';
const VITAL_CELL_CLASS_NAME = `whitespace-nowrap ${tableCellClassName}`;

function summarize(notes: string | null): string {
  if (!notes) {
    return EMPTY_VALUE;
  }

  const singleLine = notes.replace(/\s+/g, ' ').trim();
  return singleLine.length > NOTES_SUMMARY_LENGTH
    ? `${singleLine.slice(0, NOTES_SUMMARY_LENGTH).trimEnd()}…`
    : singleLine;
}

function formatMeasurement(value: number | null, fractionDigits = 0): string {
  return value === null ? EMPTY_VALUE : value.toFixed(fractionDigits);
}

function formatBloodPressure(reading: VitalSigns): string {
  return reading.systolicBloodPressure === null || reading.diastolicBloodPressure === null
    ? EMPTY_VALUE
    : `${reading.systolicBloodPressure}/${reading.diastolicBloodPressure}`;
}

const TREND_SYMBOLS = { up: '▲', down: '▼', unchanged: '=' } as const;
const TREND_WORDS = { up: 'Up', down: 'Down', unchanged: 'Unchanged' } as const;
// Colour only says "this moved"; whether a rise is good or bad depends on the metric,
// so the direction is carried by the symbol and its text, never by colour alone.
const TREND_CLASS_NAMES = {
  up: 'text-amber-700',
  down: 'text-brand-blue-dark',
  unchanged: 'text-slate-500',
} as const;

function TrendIndicator({
  readings,
  index,
  metric,
}: {
  readings: readonly VitalSigns[];
  index: number;
  metric: TrendMetric;
}) {
  const trend = trendFor(readings, index, metric);
  if (!trend) {
    return null;
  }

  return (
    <span
      className={`ml-1.5 text-xs font-bold ${TREND_CLASS_NAMES[trend.direction]}`}
      title={`${TREND_WORDS[trend.direction]} from ${trend.previousValue}`}
    >
      <span aria-hidden="true">{TREND_SYMBOLS[trend.direction]}</span>
      <span className="sr-only">{`${TREND_WORDS[trend.direction]} from ${trend.previousValue}`}</span>
    </span>
  );
}

function DetailField({ label, value }: { label: string; value: string | null }) {
  return (
    <div>
      <dt className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">{label}</dt>
      <dd className="mt-0.5 whitespace-pre-wrap break-words text-slate-900">{value ?? EMPTY_VALUE}</dd>
    </div>
  );
}

function PatientNotFoundNotice() {
  return (
    <Banner tone="error" title="Patient Not Found">
      No patient exists with this ID.
    </Banner>
  );
}

export function PatientHistoryPage() {
  const { patientId } = useParams<{ patientId: string }>();
  const { user } = useAuth();

  if (!patientId) {
    return (
      <DashboardShell
        sectionLabel="Patient History"
        backLink={{ to: user ? roleRoutes[user.role] : '/login', destination: 'Dashboard' }}
      >
        <PatientNotFoundNotice />
      </DashboardShell>
    );
  }

  // Keyed by patient so that all loaded state starts fresh for each patient.
  return <PatientHistoryContent key={patientId} patientId={patientId} />;
}

function PatientHistoryContent({ patientId }: { patientId: string }) {
  const [patientLoadState, setPatientLoadState] = useState<PatientLoadState>('loading');
  const [patient, setPatient] = useState<PatientProfile | null>(null);
  const [consultations, setConsultations] = useState<Consultation[]>([]);
  const [consultationsLoadState, setConsultationsLoadState] =
    useState<SectionLoadState>('loading');
  const [vitals, setVitals] = useState<VitalSigns[]>([]);
  const [vitalsLoadState, setVitalsLoadState] = useState<SectionLoadState>('loading');
  const [expandedIds, setExpandedIds] = useState<ReadonlySet<string>>(new Set());

  useEffect(() => {
    let disposed = false;

    getPatient(patientId)
      .then((loadedPatient) => {
        if (disposed) {
          return;
        }
        setPatient(loadedPatient);
        setPatientLoadState('loaded');
      })
      .catch((error) => {
        if (disposed) {
          return;
        }
        setPatientLoadState(
          error instanceof ApiError && error.status === 404 ? 'notFound' : 'error',
        );
      });

    getPatientConsultationHistory(patientId)
      .then((items) => {
        if (disposed) {
          return;
        }
        setConsultations(items);
        setConsultationsLoadState('loaded');
      })
      .catch(() => {
        if (disposed) {
          return;
        }
        setConsultationsLoadState('error');
      });

    getPatientVitalsHistory(patientId)
      .then((items) => {
        if (disposed) {
          return;
        }
        setVitals(items);
        setVitalsLoadState('loaded');
      })
      .catch(() => {
        if (disposed) {
          return;
        }
        setVitalsLoadState('error');
      });

    return () => {
      disposed = true;
    };
  }, [patientId]);

  function toggleExpanded(consultationId: string) {
    setExpandedIds((previous) => {
      const next = new Set(previous);
      if (next.has(consultationId)) {
        next.delete(consultationId);
      } else {
        next.add(consultationId);
      }
      return next;
    });
  }

  return (
    <DashboardShell
      sectionLabel="Patient History"
      width="wide"
      backLink={{ to: `/patients/${patientId}`, destination: 'Patient Profile' }}
    >
      {patientLoadState === 'loading' && <LoadingText>Loading patient…</LoadingText>}

      {patientLoadState === 'notFound' && <PatientNotFoundNotice />}

      {patientLoadState === 'error' && (
        <Banner tone="error" title="Unable to Load Patient">
          Something went wrong. Please try again.
        </Banner>
      )}

      {patientLoadState === 'loaded' && patient && (
        <>
          <SectionCard eyebrow="Patient" title={patient.fullName} />

          <SectionCard
            title="Consultation History"
            titleId="consultation-history-heading"
            description={
              consultationsLoadState === 'loaded' && consultations.length > 0
                ? 'Newest first. Select a visit to see its full record.'
                : undefined
            }
          >
            {consultationsLoadState === 'loading' && <LoadingText>Loading consultations…</LoadingText>}

            {consultationsLoadState === 'error' && (
              <Banner tone="error" role="alert">
                Unable to load consultation history.
              </Banner>
            )}

            {consultationsLoadState === 'loaded' && consultations.length === 0 && (
              <EmptyState>
                <p>First visit — no previous consultations</p>
              </EmptyState>
            )}

            {consultationsLoadState === 'loaded' && consultations.length > 0 && (
              <ol className="space-y-3">
                {consultations.map((consultation) => {
                  const expanded = expandedIds.has(consultation.id);
                  const detailsId = `consultation-details-${consultation.id}`;

                  return (
                    <li key={consultation.id} className="border border-slate-300">
                      <button
                        type="button"
                        onClick={() => toggleExpanded(consultation.id)}
                        aria-expanded={expanded}
                        aria-controls={detailsId}
                        className="flex w-full items-start justify-between gap-4 px-4 py-3 text-left hover:bg-slate-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
                      >
                        <span className="block min-w-0">
                          <span className="block text-sm font-semibold text-slate-900">
                            {formatDateTime(consultation.consultationDate)} · {consultation.doctorName}
                          </span>
                          <span className="mt-1 block break-words text-sm text-slate-700">
                            <span className="font-bold">Symptoms:</span> {consultation.symptoms}
                          </span>
                          <span className="mt-0.5 block break-words text-sm text-slate-700">
                            <span className="font-bold">Diagnosis:</span> {consultation.diagnosis}
                          </span>
                          <span className="mt-0.5 block break-words text-sm text-slate-600">
                            <span className="font-bold">Notes:</span> {summarize(consultation.notes)}
                          </span>
                        </span>
                        <span className="flex shrink-0 items-center gap-1.5 text-xs font-bold uppercase tracking-[0.12em] text-brand-blue">
                          {expanded ? 'Hide' : 'Details'}
                          <span aria-hidden="true">{expanded ? '▲' : '▼'}</span>
                        </span>
                      </button>

                      {expanded && (
                        <dl
                          id={detailsId}
                          className="grid gap-x-6 gap-y-4 border-t border-slate-300 bg-slate-50 px-4 py-4 text-sm sm:grid-cols-2"
                        >
                          <DetailField label="Room" value={consultation.roomNumber} />
                          <DetailField label="Template" value={consultation.templateName} />
                          <DetailField label="Symptoms" value={consultation.symptoms} />
                          <DetailField
                            label="Examination Findings"
                            value={consultation.examinationFindings}
                          />
                          <DetailField label="Diagnosis" value={consultation.diagnosis} />
                          <DetailField label="Notes" value={consultation.notes} />
                          <DetailField
                            label="Follow-up Date"
                            value={
                              consultation.followUpDate
                                ? formatDate(consultation.followUpDate.slice(0, 10))
                                : null
                            }
                          />
                          <DetailField
                            label="Follow-up Instructions"
                            value={consultation.followUpInstructions}
                          />
                        </dl>
                      )}
                    </li>
                  );
                })}
              </ol>
            )}
          </SectionCard>

          <SectionCard
            title="Vitals History"
            titleId="vitals-history-heading"
            description={
              vitalsLoadState === 'loaded' && vitals.length > 0
                ? 'Newest first. ▲ higher, ▼ lower, = unchanged, compared with the previous recorded value.'
                : undefined
            }
          >
            {vitalsLoadState === 'loading' && <LoadingText>Loading vital signs…</LoadingText>}

            {vitalsLoadState === 'error' && (
              <Banner tone="error" role="alert">
                Unable to load vitals history.
              </Banner>
            )}

            {vitalsLoadState === 'loaded' && vitals.length === 0 && (
              <EmptyState>
                <p>No vital signs recorded yet</p>
              </EmptyState>
            )}

            {vitalsLoadState === 'loaded' && vitals.length > 0 && (
              <div className={tableWrapperClassName}>
                <table className={tableClassName}>
                  <thead className={tableHeadClassName}>
                    <tr>
                      {[
                        'Date',
                        'BP (mmHg)',
                        'Temp (°C)',
                        'Pulse (bpm)',
                        'Resp. Rate (/min)',
                        'O₂ Sat (%)',
                        'Height (cm)',
                        'Weight (kg)',
                        'BMI',
                      ].map((heading) => (
                        <th key={heading} scope="col" className={tableHeaderCellClassName}>
                          {heading}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className={tableBodyClassName}>
                    {vitals.map((reading, index) => (
                      <tr key={reading.id}>
                        <td className={`whitespace-nowrap ${tableKeyCellClassName}`}>
                          {formatDateTime(reading.recordedAt)}
                        </td>
                        <td className={VITAL_CELL_CLASS_NAME}>
                          {formatBloodPressure(reading)}
                          <TrendIndicator readings={vitals} index={index} metric="systolicBloodPressure" />
                        </td>
                        <td className={VITAL_CELL_CLASS_NAME}>
                          {formatMeasurement(reading.temperatureCelsius, 1)}
                          <TrendIndicator readings={vitals} index={index} metric="temperatureCelsius" />
                        </td>
                        <td className={VITAL_CELL_CLASS_NAME}>
                          {formatMeasurement(reading.pulseRate)}
                          <TrendIndicator readings={vitals} index={index} metric="pulseRate" />
                        </td>
                        <td className={VITAL_CELL_CLASS_NAME}>
                          {formatMeasurement(reading.respiratoryRate)}
                          <TrendIndicator readings={vitals} index={index} metric="respiratoryRate" />
                        </td>
                        <td className={VITAL_CELL_CLASS_NAME}>
                          {formatMeasurement(reading.oxygenSaturation)}
                          <TrendIndicator readings={vitals} index={index} metric="oxygenSaturation" />
                        </td>
                        <td className={VITAL_CELL_CLASS_NAME}>
                          {formatMeasurement(reading.heightCentimeters, 1)}
                        </td>
                        <td className={VITAL_CELL_CLASS_NAME}>
                          {formatMeasurement(reading.weightKilograms, 1)}
                          <TrendIndicator readings={vitals} index={index} metric="weightKilograms" />
                        </td>
                        <td className={VITAL_CELL_CLASS_NAME}>
                          {formatMeasurement(reading.bmi, 1)}
                          <TrendIndicator readings={vitals} index={index} metric="bmi" />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </SectionCard>
        </>
      )}
    </DashboardShell>
  );
}
