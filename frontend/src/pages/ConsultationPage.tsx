import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  completeConsultation,
  createConsultation,
  getConsultationForQueue,
  getConsultationTemplates,
  type ConsultationProgress,
  type ConsultationTemplate,
  type CreateConsultationRequestBody,
} from '../api/consultations';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { loadCurrentPatient, type CurrentPatient } from '../consultations/currentPatient';
import { VitalSignsForm } from '../consultations/VitalSignsForm';
import { DashboardShell } from '../dashboards/DashboardShell';

type TemplateLoadState = 'loading' | 'loaded' | 'error';
type CurrentPatientLoadState = 'loading' | 'loaded' | 'error';
type SubmissionState = 'idle' | 'submitting' | 'created' | 'failed';

interface ConsultationFormState {
  templateId: string;
  symptoms: string;
  examinationFindings: string;
  diagnosis: string;
  notes: string;
  followUpDate: string;
  followUpInstructions: string;
}

interface FieldErrors {
  symptoms: string | null;
  diagnosis: string | null;
  templateId: string | null;
  followUpDate: string | null;
  followUpInstructions: string | null;
}

const EMPTY_FORM: ConsultationFormState = {
  templateId: '',
  symptoms: '',
  examinationFindings: '',
  diagnosis: '',
  notes: '',
  followUpDate: '',
  followUpInstructions: '',
};

const EMPTY_FIELD_ERRORS: FieldErrors = {
  symptoms: null,
  diagnosis: null,
  templateId: null,
  followUpDate: null,
  followUpInstructions: null,
};

const CLINIC_TIME_ZONE = 'Asia/Colombo';

function clinicTodayIsoDate(): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: CLINIC_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());
  const values = Object.fromEntries(parts.map((part) => [part.type, part.value]));
  return `${values.year}-${values.month}-${values.day}`;
}

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
    followUpDate: serverErrors.followupdate ?? previous.followUpDate,
    followUpInstructions:
      serverErrors.followupinstructions ?? previous.followUpInstructions,
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
  const navigate = useNavigate();
  const [currentPatient, setCurrentPatient] = useState<CurrentPatient | null>(null);
  const [currentPatientLoadState, setCurrentPatientLoadState] = useState<CurrentPatientLoadState>('loading');
  const [templates, setTemplates] = useState<ConsultationTemplate[]>([]);
  const [templateLoadState, setTemplateLoadState] = useState<TemplateLoadState>('loading');
  const [form, setForm] = useState<ConsultationFormState>(EMPTY_FORM);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>(EMPTY_FIELD_ERRORS);
  const [submissionState, setSubmissionState] = useState<SubmissionState>('idle');
  const [message, setMessage] = useState<string | null>(null);
  const [createdConsultation, setCreatedConsultation] = useState<ConsultationProgress | null>(null);
  const [completionState, setCompletionState] = useState<'idle' | 'completing' | 'failed'>('idle');
  const [completionError, setCompletionError] = useState<string | null>(null);

  useEffect(() => {
    let disposed = false;

    async function loadAssignment() {
      try {
        const assignment = await loadCurrentPatient();
        if (!disposed) {
          setCurrentPatient(assignment);
          if (assignment) {
            const savedConsultation = await getConsultationForQueue(assignment.queueId);
            if (disposed) {
              return;
            }

            setCreatedConsultation(savedConsultation);
            if (savedConsultation) {
              setSubmissionState('created');
            }
          }
          setCurrentPatientLoadState('loaded');
        }
      } catch {
        if (!disposed) {
          setCurrentPatientLoadState('error');
        }
      }
    }

    void loadAssignment();

    return () => {
      disposed = true;
    };
  }, [user?.userId]);

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
    const followUpInstructions = form.followUpInstructions.trim();
    const nextFieldErrors: FieldErrors = {
      symptoms: symptoms ? null : 'Symptoms are required',
      diagnosis: diagnosis ? null : 'Diagnosis is required',
      templateId: null,
      followUpDate:
        followUpInstructions && !form.followUpDate
          ? 'Follow-up date is required when instructions are provided'
          : form.followUpDate && form.followUpDate < clinicTodayIsoDate()
            ? 'Follow-up date cannot be in the past'
          : null,
      followUpInstructions:
        form.followUpDate && !followUpInstructions
          ? 'Follow-up instructions are required when a date is provided'
          : followUpInstructions.length > 500
            ? 'Follow-up instructions must be 500 characters or fewer'
            : null,
    };
    setFieldErrors(nextFieldErrors);

    if (Object.values(nextFieldErrors).some((error) => error !== null)) {
      return;
    }

    const request: CreateConsultationRequestBody = {
      queueId: currentPatient.queueId,
      patientId: currentPatient.patientId,
      symptoms,
      examinationFindings: form.examinationFindings.trim() || null,
      diagnosis,
      notes: form.notes.trim() || null,
      followUpDate: form.followUpDate || null,
      followUpInstructions: followUpInstructions || null,
      templateId: form.templateId || null,
    };

    setSubmissionState('submitting');
    setMessage(null);

    try {
      const consultation = await createConsultation(request);
      setCreatedConsultation({
        id: consultation.id,
        queueId: consultation.queueId,
        status: 'IN_PROGRESS',
        hasVitalSigns: false,
      });
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

  async function handleComplete() {
    if (!createdConsultation?.hasVitalSigns || completionState === 'completing') {
      return;
    }

    setCompletionState('completing');
    setCompletionError(null);

    try {
      await completeConsultation(createdConsultation.id);
      navigate('/doctor/prescription', {
        state: {
          completed: true,
          consultationId: createdConsultation.id,
          queueId: createdConsultation.queueId,
          patientId: currentPatient?.patientId,
          patientName: currentPatient?.patientName,
          queueNumber: currentPatient?.queueNumber,
        },
      });
    } catch (error) {
      setCompletionState('failed');
      setCompletionError(
        error instanceof ApiError && (error.status === 401 || error.status === 403)
          ? 'You are not authorized to complete this consultation.'
          : error instanceof ApiError && error.status === 409
            ? error.message
          : 'Consultation could not be completed. Please try again.',
      );
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

      {currentPatientLoadState === 'loading' ? (
        <p className="mt-6 text-sm text-slate-500">Loading your current consultation…</p>
      ) : currentPatientLoadState === 'error' ? (
        <div className="mt-6 border-t-4 border-b border-red-700 bg-red-50 px-6 py-4" role="alert">
          <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">
            Current Consultation Unavailable
          </p>
          <p className="mt-1 text-sm text-red-900">
            Unable to load your current patient. Please try again.
          </p>
        </div>
      ) : !currentPatient ? (
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

            <fieldset className="border border-slate-300 bg-slate-50 px-4 py-4">
              <legend className="px-1 text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                Follow-up (Optional)
              </legend>
              <p className="mb-4 text-xs text-slate-500">
                Provide both fields when this patient needs a follow-up.
              </p>

              <div>
                <label htmlFor="followUpDate" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                  Follow-up Date
                </label>
                <input
                  id="followUpDate"
                  name="followUpDate"
                  type="date"
                  min={clinicTodayIsoDate()}
                  value={form.followUpDate}
                  onChange={(event) => {
                    setForm((previous) => ({ ...previous, followUpDate: event.target.value }));
                    clearFieldError('followUpDate');
                  }}
                  disabled={isBusy || submissionState === 'created'}
                  aria-invalid={fieldErrors.followUpDate ? true : undefined}
                  aria-describedby={fieldErrors.followUpDate ? 'followUpDate-error' : undefined}
                  className={inputClassName(!!fieldErrors.followUpDate)}
                />
                {fieldErrors.followUpDate && (
                  <p id="followUpDate-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                    {fieldErrors.followUpDate}
                  </p>
                )}
              </div>

              <div className="mt-4">
                <label htmlFor="followUpInstructions" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                  Follow-up Instructions
                </label>
                <textarea
                  id="followUpInstructions"
                  name="followUpInstructions"
                  rows={2}
                  maxLength={500}
                  value={form.followUpInstructions}
                  onChange={(event) => {
                    setForm((previous) => ({
                      ...previous,
                      followUpInstructions: event.target.value,
                    }));
                    clearFieldError('followUpInstructions');
                  }}
                  disabled={isBusy || submissionState === 'created'}
                  aria-invalid={fieldErrors.followUpInstructions ? true : undefined}
                  aria-describedby={fieldErrors.followUpInstructions ? 'followUpInstructions-error' : undefined}
                  className={inputClassName(!!fieldErrors.followUpInstructions)}
                />
                {fieldErrors.followUpInstructions && (
                  <p id="followUpInstructions-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                    {fieldErrors.followUpInstructions}
                  </p>
                )}
              </div>
            </fieldset>

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

          {submissionState === 'created' && createdConsultation && (
            <>
              <VitalSignsForm
                consultationId={createdConsultation.id}
                alreadySaved={createdConsultation.hasVitalSigns}
                onSaved={() => setCreatedConsultation((previous) => previous
                  ? { ...previous, hasVitalSigns: true }
                  : previous)}
              />

              <section className="mt-6 border border-slate-300 bg-white px-6 py-6" aria-labelledby="complete-consultation-heading">
                <h2 id="complete-consultation-heading" className="text-xl font-semibold text-slate-900">
                  Complete Consultation
                </h2>
                {!createdConsultation.hasVitalSigns && (
                  <p className="mt-2 text-sm text-amber-800">Please save vital signs first</p>
                )}
                {completionError && (
                  <p className="mt-3 border-l-2 border-red-700 bg-red-50 px-3 py-2 text-sm text-red-900" role="alert">
                    {completionError}
                  </p>
                )}
                <button
                  type="button"
                  disabled={!createdConsultation.hasVitalSigns || completionState === 'completing'}
                  onClick={() => void handleComplete()}
                  className="mt-4 bg-brand-blue px-4 py-3 text-sm font-bold uppercase tracking-[0.12em] text-white hover:bg-brand-blue-dark disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {completionState === 'completing' ? 'Completing...' : 'Complete Consultation'}
                </button>
              </section>
            </>
          )}
        </>
      )}
    </DashboardShell>
  );
}
