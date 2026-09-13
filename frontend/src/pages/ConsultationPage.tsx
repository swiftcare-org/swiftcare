import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  createConsultation,
  getConsultationTemplates,
  type Consultation,
  type ConsultationTemplate,
  type CreateConsultationRequestBody,
} from '../api/consultations';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { readStoredCurrentPatient } from '../consultations/currentPatientStorage';
import { DashboardShell } from '../dashboards/DashboardShell';

type TemplateLoadState = 'loading' | 'loaded' | 'error';
type SubmissionState = 'idle' | 'submitting' | 'created' | 'failed';

interface ConsultationFormState {
  templateId: string;
  symptoms: string;
  examinationFindings: string;
  diagnosis: string;
  notes: string;
}

interface FieldErrors {
  symptoms: string | null;
  diagnosis: string | null;
  templateId: string | null;
}

const EMPTY_FORM: ConsultationFormState = {
  templateId: '',
  symptoms: '',
  examinationFindings: '',
  diagnosis: '',
  notes: '',
};

const EMPTY_FIELD_ERRORS: FieldErrors = {
  symptoms: null,
  diagnosis: null,
  templateId: null,
};

function inputClassName(hasError: boolean): string {
  return `mt-1.5 block w-full border-2 bg-white px-3 py-2.5 text-sm text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:bg-slate-100 disabled:text-slate-400 ${
    hasError ? 'border-red-600' : 'border-slate-400 focus:border-brand-blue'
  }`;
}

function applyServerFieldErrors(
  previous: FieldErrors,
  serverErrors: Readonly<Record<string, string>>,
): FieldErrors {
  return {
    symptoms: serverErrors.symptoms ?? previous.symptoms,
    diagnosis: serverErrors.diagnosis ?? previous.diagnosis,
    templateId: serverErrors.templateid ?? previous.templateId,
  };
}

function consultationErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401 || error.status === 403) {
      return 'You are not authorized to create consultation records.';
    }

    if (error.status === 409) {
      return error.message;
    }
  }

  return 'Unable to save the consultation record. Please try again.';
}

export function ConsultationPage() {
  const { user } = useAuth();
  const currentPatient = readStoredCurrentPatient(user?.userId);
  const [templates, setTemplates] = useState<ConsultationTemplate[]>([]);
  const [templateLoadState, setTemplateLoadState] = useState<TemplateLoadState>('loading');
  const [form, setForm] = useState<ConsultationFormState>(EMPTY_FORM);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>(EMPTY_FIELD_ERRORS);
  const [submissionState, setSubmissionState] = useState<SubmissionState>('idle');
  const [message, setMessage] = useState<string | null>(null);
  const [createdConsultation, setCreatedConsultation] = useState<Consultation | null>(null);

  useEffect(() => {
    let disposed = false;

    async function loadTemplates() {
      try {
        const loadedTemplates = await getConsultationTemplates();
        if (!disposed) {
          setTemplates(loadedTemplates);
          setTemplateLoadState('loaded');
        }
      } catch {
        if (!disposed) {
          setTemplateLoadState('error');
        }
      }
    }

    void loadTemplates();

    return () => {
      disposed = true;
    };
  }, []);

  const isBusy = submissionState === 'submitting';

  function clearFieldError(field: keyof FieldErrors) {
    setFieldErrors((previous) =>
      previous[field] ? { ...previous, [field]: null } : previous,
    );
    if (submissionState === 'failed') {
      setSubmissionState('idle');
      setMessage(null);
    }
  }

  function handleTemplateChange(templateId: string) {
    clearFieldError('templateId');

    if (!templateId) {
      setForm((previous) => ({ ...previous, templateId: '' }));
      return;
    }

    const selectedTemplate = templates.find((template) => template.id === templateId);
    if (!selectedTemplate) {
      setForm((previous) => ({ ...previous, templateId }));
      return;
    }

    setForm((previous) => ({
      ...previous,
      templateId,
      symptoms: selectedTemplate.symptoms,
      examinationFindings: selectedTemplate.examinationFindings,
      notes: selectedTemplate.notes,
    }));
    setFieldErrors((previous) => ({ ...previous, symptoms: null }));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    if (!currentPatient) {
      return;
    }

    const symptoms = form.symptoms.trim();
    const diagnosis = form.diagnosis.trim();
    const nextFieldErrors: FieldErrors = {
      symptoms: symptoms ? null : 'Symptoms are required',
      diagnosis: diagnosis ? null : 'Diagnosis is required',
      templateId: null,
    };
    setFieldErrors(nextFieldErrors);

    if (nextFieldErrors.symptoms || nextFieldErrors.diagnosis) {
      return;
    }

    const request: CreateConsultationRequestBody = {
      queueId: currentPatient.queueId,
      patientId: currentPatient.patientId,
      symptoms,
      examinationFindings: form.examinationFindings.trim() || null,
      diagnosis,
      notes: form.notes.trim() || null,
      templateId: form.templateId || null,
    };

    setSubmissionState('submitting');
    setMessage(null);

    try {
      const consultation = await createConsultation(request);
      setCreatedConsultation(consultation);
      setSubmissionState('created');
    } catch (error) {
      setCreatedConsultation(null);
      setSubmissionState('failed');
      setMessage(consultationErrorMessage(error));

      if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
        setFieldErrors((previous) => applyServerFieldErrors(previous, error.fieldErrors));
      }
    }
  }

  return (
    <DashboardShell sectionLabel="Create Consultation Record">
      <Link
        to="/doctor"
        className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
      >
        &larr; Back to Doctor Dashboard
      </Link>

      {!currentPatient ? (
        <div className="mt-6 border-t-4 border-b border-amber-600 bg-amber-50 px-6 py-4" role="alert">
          <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-amber-800">
            No Current Patient
          </p>
          <p className="mt-1 text-sm text-amber-900">
            Call a patient before recording a consultation.
          </p>
        </div>
      ) : (
        <>
          <section className="mt-6 border-t-4 border-b border-brand-blue bg-blue-50 px-6 py-4">
            <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-brand-blue-dark">
              Current Consultation
            </p>
            <p className="mt-1 text-lg font-semibold text-slate-900">
              {currentPatient.queueNumber} {currentPatient.patientName}
            </p>
            <p className="mt-1 text-xs text-slate-600">Room {currentPatient.roomNumber}</p>
          </section>

          <div aria-live="polite">
            {submissionState === 'created' && createdConsultation && (
              <div className="mt-6 border-t-4 border-b border-emerald-700 bg-emerald-50 px-6 py-3">
                <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-emerald-800">
                  Consultation Saved
                </p>
                <p className="mt-1 text-sm text-emerald-900">
                  Consultation record saved successfully for {currentPatient.queueNumber}.
                </p>
              </div>
            )}

            {submissionState === 'failed' && message && (
              <div className="mt-6 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3" role="alert">
                <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">
                  Consultation Not Saved
                </p>
                <p className="mt-1 text-sm text-red-900">{message}</p>
              </div>
            )}

            {templateLoadState === 'error' && (
              <div className="mt-6 border-t-4 border-b border-amber-600 bg-amber-50 px-6 py-3" role="alert">
                <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-amber-800">
                  Templates Unavailable
                </p>
                <p className="mt-1 text-sm text-amber-900">
                  Templates could not be loaded. You can still complete the form manually.
                </p>
              </div>
            )}
          </div>

          <form
            onSubmit={handleSubmit}
            noValidate
            className="mt-6 space-y-5 border border-slate-300 bg-white px-6 py-6"
          >
            <div>
              <label htmlFor="templateId" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                Consultation Template
              </label>
              <select
                id="templateId"
                name="templateId"
                value={form.templateId}
                onChange={(event) => handleTemplateChange(event.target.value)}
                disabled={isBusy || submissionState === 'created' || templateLoadState !== 'loaded'}
                aria-invalid={fieldErrors.templateId ? true : undefined}
                aria-describedby={fieldErrors.templateId ? 'templateId-error' : undefined}
                className={inputClassName(!!fieldErrors.templateId)}
              >
                <option value="">
                  {templateLoadState === 'loading' ? 'Loading templates...' : 'No template'}
                </option>
                {templates.map((template) => (
                  <option key={template.id} value={template.id}>
                    {template.name}
                  </option>
                ))}
              </select>
              {fieldErrors.templateId && (
                <p id="templateId-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                  {fieldErrors.templateId}
                </p>
              )}
              <p className="mt-1 text-xs text-slate-500">
                Selecting a template pre-fills editable clinical notes.
              </p>
            </div>

            <div>
              <label htmlFor="symptoms" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                Symptoms
              </label>
              <textarea
                id="symptoms"
                name="symptoms"
                rows={4}
                value={form.symptoms}
                onChange={(event) => {
                  setForm((previous) => ({ ...previous, symptoms: event.target.value }));
                  clearFieldError('symptoms');
                }}
                disabled={isBusy || submissionState === 'created'}
                aria-required="true"
                aria-invalid={fieldErrors.symptoms ? true : undefined}
                aria-describedby={fieldErrors.symptoms ? 'symptoms-error' : undefined}
                className={inputClassName(!!fieldErrors.symptoms)}
              />
              {fieldErrors.symptoms && (
                <p id="symptoms-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                  {fieldErrors.symptoms}
                </p>
              )}
            </div>

            <div>
              <label htmlFor="examinationFindings" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                Examination Findings
              </label>
              <textarea
                id="examinationFindings"
                name="examinationFindings"
                rows={4}
                value={form.examinationFindings}
                onChange={(event) =>
                  setForm((previous) => ({
                    ...previous,
                    examinationFindings: event.target.value,
                  }))
                }
                disabled={isBusy || submissionState === 'created'}
                className={inputClassName(false)}
              />
            </div>

            <div>
              <label htmlFor="diagnosis" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                Diagnosis
              </label>
              <textarea
                id="diagnosis"
                name="diagnosis"
                rows={3}
                value={form.diagnosis}
                onChange={(event) => {
                  setForm((previous) => ({ ...previous, diagnosis: event.target.value }));
                  clearFieldError('diagnosis');
                }}
                disabled={isBusy || submissionState === 'created'}
                aria-required="true"
                aria-invalid={fieldErrors.diagnosis ? true : undefined}
                aria-describedby={fieldErrors.diagnosis ? 'diagnosis-error' : undefined}
                className={inputClassName(!!fieldErrors.diagnosis)}
              />
              {fieldErrors.diagnosis && (
                <p id="diagnosis-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                  {fieldErrors.diagnosis}
                </p>
              )}
            </div>

            <div>
              <label htmlFor="notes" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                Notes
              </label>
              <textarea
                id="notes"
                name="notes"
                rows={4}
                value={form.notes}
                onChange={(event) =>
                  setForm((previous) => ({ ...previous, notes: event.target.value }))
                }
                disabled={isBusy || submissionState === 'created'}
                className={inputClassName(false)}
              />
            </div>

            <button
              type="submit"
              disabled={isBusy || submissionState === 'created'}
              className="relative w-full overflow-hidden bg-brand-blue px-4 py-3 text-sm font-bold uppercase tracking-[0.15em] text-white hover:bg-brand-blue-dark focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {isBusy ? 'Saving...' : submissionState === 'created' ? 'Consultation Saved' : 'Save Consultation'}
              {isBusy && (
                <span className="absolute inset-x-0 bottom-0 block h-0.5 overflow-hidden bg-white/20" aria-hidden="true">
                  <span className="block h-full w-1/3 animate-[loading-sweep_1.1s_ease-in-out_infinite] bg-white" />
                </span>
              )}
            </button>
          </form>
        </>
      )}
    </DashboardShell>
  );
}
