import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';
import { ApiError } from '../api/client';
import { getPatient } from '../api/patients';
import type { PatientProfile } from '../api/patients';
import { getPatientConsultationHistory } from '../api/consultations';
import type { Consultation } from '../api/consultations';

type PatientLoadState = 'loading' | 'loaded' | 'notFound' | 'error';
type SectionLoadState = 'loading' | 'loaded' | 'error';

const NOTES_SUMMARY_LENGTH = 80;

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString();
}

function formatDateOnly(value: string): string {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString();
}

function summarize(notes: string | null): string {
  if (!notes) {
    return '—';
  }

  const singleLine = notes.replace(/\s+/g, ' ').trim();
  return singleLine.length > NOTES_SUMMARY_LENGTH
    ? `${singleLine.slice(0, NOTES_SUMMARY_LENGTH).trimEnd()}…`
    : singleLine;
}

function DetailField({ label, value }: { label: string; value: string | null }) {
  return (
    <div>
      <dt className="text-xs font-bold uppercase tracking-widest text-slate-500">{label}</dt>
      <dd className="mt-0.5 whitespace-pre-wrap text-slate-800">{value ?? '—'}</dd>
    </div>
  );
}

function PatientNotFoundNotice() {
  return (
    <div className="mt-6 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3">
      <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">
        Patient Not Found
      </p>
      <p className="mt-1 text-sm text-red-900">No patient exists with this ID.</p>
    </div>
  );
}

export function PatientHistoryPage() {
  const { patientId } = useParams<{ patientId: string }>();

  if (!patientId) {
    return (
      <DashboardShell sectionLabel="Patient History">
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
    <DashboardShell sectionLabel="Patient History">
      <Link
        to={`/patients/${patientId}`}
        className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
      >
        ← Back to Patient Profile
      </Link>

      {patientLoadState === 'loading' && (
        <p className="mt-6 text-sm text-slate-500">Loading patient…</p>
      )}

      {patientLoadState === 'notFound' && <PatientNotFoundNotice />}

      {patientLoadState === 'error' && (
        <div className="mt-6 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3">
          <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">
            Unable to Load Patient
          </p>
          <p className="mt-1 text-sm text-red-900">Something went wrong. Please try again.</p>
        </div>
      )}

      {patientLoadState === 'loaded' && patient && (
        <>
          <div className="mt-6 border border-slate-300 bg-white px-6 py-6">
            <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">Patient</p>
            <p className="mt-1 text-2xl font-semibold text-slate-900">{patient.fullName}</p>
          </div>

          <section
            className="mt-6 border border-slate-300 bg-white px-6 py-6"
            aria-labelledby="consultation-history-heading"
          >
            <h2
              id="consultation-history-heading"
              className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500"
            >
              Consultation History
            </h2>

            {consultationsLoadState === 'loading' && (
              <p className="mt-3 text-sm text-slate-500">Loading consultations…</p>
            )}

            {consultationsLoadState === 'error' && (
              <p className="mt-3 border-l-2 border-red-600 pl-2 text-sm text-red-700" role="alert">
                Unable to load consultation history.
              </p>
            )}

            {consultationsLoadState === 'loaded' && consultations.length === 0 && (
              <p className="mt-3 text-sm text-slate-500">
                First visit — no previous consultations
              </p>
            )}

            {consultationsLoadState === 'loaded' && consultations.length > 0 && (
              <ol className="mt-4 space-y-3">
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
                            {formatDateTime(consultation.consultationDate)} — {consultation.doctorName}
                          </span>
                          <span className="mt-1 block text-sm text-slate-700">
                            <span className="font-bold">Symptoms:</span> {consultation.symptoms}
                          </span>
                          <span className="mt-0.5 block text-sm text-slate-700">
                            <span className="font-bold">Diagnosis:</span> {consultation.diagnosis}
                          </span>
                          <span className="mt-0.5 block text-sm text-slate-600">
                            <span className="font-bold">Notes:</span> {summarize(consultation.notes)}
                          </span>
                        </span>
                        <span className="shrink-0 text-xs font-bold uppercase tracking-[0.12em] text-brand-blue">
                          {expanded ? 'Hide' : 'Details'}
                        </span>
                      </button>

                      {expanded && (
                        <dl
                          id={detailsId}
                          className="grid gap-x-6 gap-y-3 border-t border-slate-300 bg-slate-50 px-4 py-4 text-sm sm:grid-cols-2"
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
                                ? formatDateOnly(consultation.followUpDate)
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
          </section>
        </>
      )}
    </DashboardShell>
  );
}
