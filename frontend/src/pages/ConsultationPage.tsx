import { useEffect, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
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
import { Banner } from '../components/ui/Banner';
import { Button, ButtonLink } from '../components/ui/Button';
import { ConfirmPanel } from '../components/ui/ConfirmPanel';
import { LoadingText } from '../components/ui/Feedback';
import { Field, OptionalMark, RequiredLegend } from '../components/ui/Field';
import { SectionCard } from '../components/ui/SectionCard';
import { DashboardShell } from '../dashboards/DashboardShell';
import { clinicTodayForDateInput, formatDate } from '../lib/format';

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
  const [isConfirmingCompletion, setIsConfirmingCompletion] = useState(false);
  // True when the consultation was saved in an earlier visit to this page, so this
  // session never held the values that were entered.
  const [wasRecovered, setWasRecovered] = useState(false);

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
              setWasRecovered(true);
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
          : form.followUpDate && form.followUpDate < clinicTodayForDateInput()
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

  const isSaved = submissionState === 'created';
  const fieldsLocked = isBusy || isSaved;

  return (
    <DashboardShell
      sectionLabel="Create Consultation Record"
      backLink={{ to: '/doctor', destination: 'Dashboard' }}
    >
      {currentPatientLoadState === 'loading' ? (
        <LoadingText>Loading your current consultation…</LoadingText>
      ) : currentPatientLoadState === 'error' ? (
        <Banner tone="error" title="Current Consultation Unavailable" role="alert">
          Unable to load your current patient. Please try again.
        </Banner>
      ) : !currentPatient ? (
        <Banner tone="warning" title="No Current Patient" role="alert">
          <p>Call a patient before recording a consultation.</p>
          <ButtonLink to="/doctor" variant="secondary" size="sm" className="mt-3">
            Go to Waiting Pool
          </ButtonLink>
        </Banner>
      ) : (
        <>
          <section>
            <Banner tone="info" title="Current Consultation">
              <p className="break-words text-lg font-semibold text-slate-900">
                {currentPatient.queueNumber} {currentPatient.patientName}
              </p>
              <p className="mt-1 text-xs text-slate-600">Room {currentPatient.roomNumber}</p>
            </Banner>
          </section>

          <div aria-live="polite" className="space-y-3 empty:hidden">
            {isSaved && createdConsultation && (
              <Banner tone="success" title="Consultation Saved">
                {wasRecovered
                  ? `The consultation record for ${currentPatient.queueNumber} was saved earlier and can no longer be edited here. Continue with the steps below.`
                  : `Consultation record saved successfully for ${currentPatient.queueNumber}.`}
              </Banner>
            )}

            {submissionState === 'failed' && message && (
              <Banner tone="error" title="Consultation Not Saved" role="alert">
                {message}
              </Banner>
            )}

            {templateLoadState === 'error' && !isSaved && (
              <Banner tone="warning" title="Templates Unavailable" role="alert">
                Templates could not be loaded. You can still complete the form manually.
              </Banner>
            )}
          </div>

          {/* A recovered consultation has no values to show, so its form is left out
              rather than rendered as empty, locked fields. */}
          {!wasRecovered && (
            <SectionCard eyebrow="Step 1 of 3" title="Consultation Record">
              <form onSubmit={handleSubmit} noValidate className="max-w-3xl space-y-5">
                <RequiredLegend />

                <Field
                  id="templateId"
                  label="Consultation Template"
                  optional
                  hint="Selecting a template pre-fills editable clinical notes."
                  error={fieldErrors.templateId}
                >
                  {(control) => (
                    <select
                      {...control}
                      name="templateId"
                      value={form.templateId}
                      onChange={(event) => handleTemplateChange(event.target.value)}
                      disabled={fieldsLocked || templateLoadState !== 'loaded'}
                    >
                      <option value="">
                        {templateLoadState === 'loading' ? 'Loading templates…' : 'No template'}
                      </option>
                      {templates.map((template) => (
                        <option key={template.id} value={template.id}>
                          {template.name}
                        </option>
                      ))}
                    </select>
                  )}
                </Field>

                <Field id="symptoms" label="Symptoms" required error={fieldErrors.symptoms}>
                  {(control) => (
                    <textarea
                      {...control}
                      name="symptoms"
                      rows={4}
                      value={form.symptoms}
                      onChange={(event) => {
                        setForm((previous) => ({ ...previous, symptoms: event.target.value }));
                        clearFieldError('symptoms');
                      }}
                      disabled={fieldsLocked}
                    />
                  )}
                </Field>

                <Field id="examinationFindings" label="Examination Findings" optional>
                  {(control) => (
                    <textarea
                      {...control}
                      name="examinationFindings"
                      rows={4}
                      value={form.examinationFindings}
                      onChange={(event) =>
                        setForm((previous) => ({
                          ...previous,
                          examinationFindings: event.target.value,
                        }))
                      }
                      disabled={fieldsLocked}
                    />
                  )}
                </Field>

                <Field id="diagnosis" label="Diagnosis" required error={fieldErrors.diagnosis}>
                  {(control) => (
                    <textarea
                      {...control}
                      name="diagnosis"
                      rows={3}
                      value={form.diagnosis}
                      onChange={(event) => {
                        setForm((previous) => ({ ...previous, diagnosis: event.target.value }));
                        clearFieldError('diagnosis');
                      }}
                      disabled={fieldsLocked}
                    />
                  )}
                </Field>

                <Field id="notes" label="Notes" optional>
                  {(control) => (
                    <textarea
                      {...control}
                      name="notes"
                      rows={4}
                      value={form.notes}
                      onChange={(event) =>
                        setForm((previous) => ({ ...previous, notes: event.target.value }))
                      }
                      disabled={fieldsLocked}
                    />
                  )}
                </Field>

                <fieldset className="rounded-md border border-slate-200 bg-slate-50 px-4 pb-4 pt-2">
                  <legend className="px-1.5 text-sm font-semibold text-slate-700">
                    Follow-up <OptionalMark />
                  </legend>
                  <p className="text-xs text-slate-500">
                    Fill in both fields if this patient needs a follow-up, or leave both empty.
                  </p>

                  <div className="mt-4 grid gap-5 sm:grid-cols-[minmax(0,14rem)_minmax(0,1fr)]">
                    <Field
                      id="followUpDate"
                      label="Follow-up Date"
                      hint={form.followUpDate ? `Selected: ${formatDate(form.followUpDate)}` : 'Today or later.'}
                      error={fieldErrors.followUpDate}
                    >
                      {(control) => (
                        <input
                          {...control}
                          name="followUpDate"
                          type="date"
                          min={clinicTodayForDateInput()}
                          value={form.followUpDate}
                          onChange={(event) => {
                            setForm((previous) => ({ ...previous, followUpDate: event.target.value }));
                            clearFieldError('followUpDate');
                          }}
                          disabled={fieldsLocked}
                        />
                      )}
                    </Field>

                    <Field
                      id="followUpInstructions"
                      label="Follow-up Instructions"
                      hint="Up to 500 characters."
                      error={fieldErrors.followUpInstructions}
                    >
                      {(control) => (
                        <textarea
                          {...control}
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
                          disabled={fieldsLocked}
                        />
                      )}
                    </Field>
                  </div>
                </fieldset>

                <Button type="submit" fullWidth loading={isBusy} disabled={isSaved}>
                  {isBusy ? 'Saving…' : isSaved ? 'Consultation Saved' : 'Save Consultation'}
                </Button>
              </form>
            </SectionCard>
          )}

          {isSaved && createdConsultation && (
            <>
              <VitalSignsForm
                consultationId={createdConsultation.id}
                alreadySaved={createdConsultation.hasVitalSigns}
                onSaved={() => setCreatedConsultation((previous) => previous
                  ? { ...previous, hasVitalSigns: true }
                  : previous)}
              />

              <SectionCard
                eyebrow="Step 3 of 3"
                title="Complete Consultation"
                titleId="complete-consultation-heading"
                description="Ends this visit and takes you to the prescription."
              >
                {!createdConsultation.hasVitalSigns && (
                  <p className="text-sm font-medium text-amber-700">
                    Please save vital signs first
                  </p>
                )}

                {isConfirmingCompletion ? (
                  <ConfirmPanel
                    labelId="confirm-completion-title"
                    title="Complete This Consultation?"
                    tone="primary"
                    confirmLabel="Confirm Completion"
                    busyLabel="Completing…"
                    busy={completionState === 'completing'}
                    error={completionError}
                    onConfirm={() => void handleComplete()}
                    onCancel={() => {
                      setIsConfirmingCompletion(false);
                      setCompletionError(null);
                    }}
                  >
                    The consultation record and vital signs for {currentPatient.queueNumber} can no longer be
                    changed after this, and the patient leaves your room.
                  </ConfirmPanel>
                ) : (
                  <Button
                    className={createdConsultation.hasVitalSigns ? '' : 'mt-4'}
                    disabled={!createdConsultation.hasVitalSigns}
                    onClick={() => setIsConfirmingCompletion(true)}
                  >
                    Complete Consultation
                  </Button>
                )}
              </SectionCard>
            </>
          )}
        </>
      )}
    </DashboardShell>
  );
}
