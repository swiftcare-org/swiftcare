import { useEffect, useState, type FormEvent } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { getAllergies, type Allergy } from '../api/allergies';
import { ApiError } from '../api/client';
import {
  addPrescriptionMedicine,
  createPrescription,
  getPatientPrescriptions,
  getPrescriptionByQueueId,
  recordNoPrescriptionRequired,
  removePrescriptionMedicine,
  type Prescription,
  type PrescriptionMedicineInput,
} from '../api/prescriptions';
import { AlertBanner } from '../components/AlertBanner';
import { Banner } from '../components/ui/Banner';
import { Button, ButtonLink } from '../components/ui/Button';
import { ConfirmPanel } from '../components/ui/ConfirmPanel';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { Field, RequiredLegend } from '../components/ui/Field';
import { SectionCard } from '../components/ui/SectionCard';
import { DashboardShell } from '../dashboards/DashboardShell';
import { formatDate, formatTime } from '../lib/format';
import { useAuth } from '../auth/useAuth';
import {
  findPendingPrescriptionContext,
  isPrescriptionContext,
  type PrescriptionContext,
} from '../prescriptions/pendingPrescription';
import {
  NO_PRESCRIPTIONS_MESSAGE,
  PrescriptionHistoryList,
} from '../prescriptions/PrescriptionHistoryList';

interface MedicineDraft extends Omit<PrescriptionMedicineInput, 'instructions'> {
  clientId: string;
  instructions: string;
}

type LoadState = 'loading' | 'loaded' | 'error';
type ContextLoadState = 'loading' | 'loaded' | 'error';
type SubmissionState = 'idle' | 'submitting' | 'saved' | 'failed';
type ItemChangeState = 'idle' | 'adding' | 'removing';

interface RemovalTarget {
  source: 'draft' | 'saved';
  id: string;
  medicineName: string;
}

type RequiredMedicineField = 'medicineName' | 'dosage' | 'frequency' | 'duration';

const REQUIRED_MEDICINE_FIELDS: {
  field: RequiredMedicineField;
  label: string;
  maxLength: number;
  hint?: string;
}[] = [
  { field: 'medicineName', label: 'Medicine name', maxLength: 200 },
  { field: 'dosage', label: 'Dosage', maxLength: 100, hint: 'For example 500 mg.' },
  { field: 'frequency', label: 'Frequency', maxLength: 100, hint: 'For example twice a day.' },
  { field: 'duration', label: 'Duration', maxLength: 100, hint: 'For example 5 days.' },
];

function newMedicine(): MedicineDraft {
  return {
    clientId: crypto.randomUUID(),
    medicineName: '',
    dosage: '',
    frequency: '',
    duration: '',
    instructions: '',
  };
}

function noPrescriptionErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401 || error.status === 403) {
      return 'You are not authorized to record this.';
    }

    if (error.status === 409) {
      return error.message;
    }
  }

  return 'Unable to record this. Please try again.';
}

export function PrescriptionPage() {
  const { user } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const navigatedContext = isPrescriptionContext(location.state) ? location.state : null;
  const [context, setContext] = useState<PrescriptionContext | null>(navigatedContext);
  const [contextLoadState, setContextLoadState] = useState<ContextLoadState>(
    navigatedContext ? 'loaded' : 'loading',
  );
  const patientId = context?.patientId;
  const consultationId = context?.consultationId;
  const queueId = context?.queueId;
  const [medicines, setMedicines] = useState<MedicineDraft[]>([]);
  const [allergies, setAllergies] = useState<Allergy[]>([]);
  const [history, setHistory] = useState<Prescription[]>([]);
  const [referenceLoadState, setReferenceLoadState] = useState<LoadState>('loading');
  const [submissionState, setSubmissionState] = useState<SubmissionState>('idle');
  const [message, setMessage] = useState<string | null>(null);
  const [savedPrescription, setSavedPrescription] = useState<Prescription | null>(null);
  const [additionDraft, setAdditionDraft] = useState<MedicineDraft | null>(null);
  const [removalTarget, setRemovalTarget] = useState<RemovalTarget | null>(null);
  const [itemChangeState, setItemChangeState] = useState<ItemChangeState>('idle');
  const [itemMessage, setItemMessage] = useState<string | null>(null);
  const [itemMessageTone, setItemMessageTone] = useState<'success' | 'error'>('success');
  // Set once a submit finds gaps, so the empty required fields are pointed out one by one.
  const [draftValidated, setDraftValidated] = useState(false);
  const [additionValidated, setAdditionValidated] = useState(false);
  // Set when this consultation was already recorded as needing no prescription.
  const [noPrescription, setNoPrescription] = useState<Prescription | null>(null);
  const [noPrescriptionState, setNoPrescriptionState] = useState<'idle' | 'confirming' | 'submitting'>('idle');
  const [noPrescriptionError, setNoPrescriptionError] = useState<string | null>(null);

  const hasPrescriptionContext = isPrescriptionContext(context);

  useEffect(() => {
    if (navigatedContext) {
      return;
    }

    let disposed = false;

    async function recoverContext() {
      try {
        const recovered = await findPendingPrescriptionContext();
        if (!disposed) {
          setContext(recovered);
          setContextLoadState('loaded');
        }
      } catch {
        if (!disposed) {
          setContextLoadState('error');
        }
      }
    }

    void recoverContext();

    return () => {
      disposed = true;
    };
  }, [navigatedContext, user?.userId]);

  useEffect(() => {
    if (!patientId) {
      return;
    }

    let disposed = false;

    async function loadReferenceData() {
      const [allergyResult, historyResult, outcomeResult] = await Promise.allSettled([
        getAllergies(patientId!),
        getPatientPrescriptions(patientId!),
        // 404 simply means nothing is recorded for this visit yet.
        queueId ? getPrescriptionByQueueId(queueId) : Promise.reject(new Error('No queue entry')),
      ]);

      if (disposed) {
        return;
      }

      if (allergyResult.status === 'fulfilled') {
        setAllergies(allergyResult.value);
      }

      if (historyResult.status === 'fulfilled') {
        setHistory(historyResult.value);
        const currentPrescription = historyResult.value.find(
          (prescription) => prescription.consultationId === consultationId,
        );
        if (currentPrescription) {
          setSavedPrescription(currentPrescription);
          setSubmissionState('saved');
        }
      }

      if (outcomeResult.status === 'fulfilled' && outcomeResult.value.status === 'NOT_REQUIRED') {
        setNoPrescription(outcomeResult.value);
      }

      setReferenceLoadState(
        allergyResult.status === 'fulfilled' && historyResult.status === 'fulfilled'
          ? 'loaded'
          : 'error',
      );
    }

    void loadReferenceData();

    return () => {
      disposed = true;
    };
  }, [consultationId, patientId, queueId]);

  function updateMedicine(
    clientId: string,
    field: keyof Omit<MedicineDraft, 'clientId'>,
    value: string,
  ) {
    setMedicines((current) =>
      current.map((medicine) =>
        medicine.clientId === clientId ? { ...medicine, [field]: value } : medicine,
      ),
    );
    if (submissionState === 'failed') {
      setSubmissionState('idle');
      setMessage(null);
    }
  }

  function updateAdditionDraft(
    field: keyof Omit<MedicineDraft, 'clientId'>,
    value: string,
  ) {
    setAdditionDraft((current) => current ? { ...current, [field]: value } : current);
    setItemMessage(null);
  }

  function applyUpdatedPrescription(prescription: Prescription) {
    setSavedPrescription(prescription);
    setHistory((current) => [
      prescription,
      ...current.filter((item) => item.id !== prescription.id),
    ]);
  }

  function itemChangeErrorMessage(error: unknown): string {
    if (error instanceof ApiError) {
      if (error.status === 401 || error.status === 403) {
        return 'You are not authorized to modify this prescription.';
      }

      if (error.status === 404 || error.status === 409) {
        return error.message;
      }
    }

    return 'Unable to update the prescription. Please try again.';
  }

  function applyDispensedState(error: unknown) {
    if (
      error instanceof ApiError
      && error.status === 409
      && error.message === 'Cannot modify a dispensed prescription'
    ) {
      setSavedPrescription((current) => current ? { ...current, status: 'DISPENSED' } : current);
    }
  }

  async function handleAddSavedMedicine(event: FormEvent) {
    event.preventDefault();
    if (!savedPrescription || !additionDraft || itemChangeState !== 'idle') {
      return;
    }

    if (savedPrescription.status === 'DISPENSED') {
      setItemMessageTone('error');
      setItemMessage('Cannot modify a dispensed prescription');
      return;
    }

    if (
      !additionDraft.medicineName.trim()
      || !additionDraft.dosage.trim()
      || !additionDraft.frequency.trim()
      || !additionDraft.duration.trim()
    ) {
      setAdditionValidated(true);
      setItemMessageTone('error');
      setItemMessage('Complete the medicine name, dosage, frequency, and duration');
      return;
    }

    setItemChangeState('adding');
    setItemMessage(null);

    try {
      const prescription = await addPrescriptionMedicine(savedPrescription.id, {
        medicineName: additionDraft.medicineName.trim(),
        dosage: additionDraft.dosage.trim(),
        frequency: additionDraft.frequency.trim(),
        duration: additionDraft.duration.trim(),
        instructions: additionDraft.instructions.trim() || null,
      });
      applyUpdatedPrescription(prescription);
      setAdditionDraft(null);
      setItemMessageTone('success');
      setItemMessage('Medicine added successfully.');
    } catch (error) {
      applyDispensedState(error);
      setItemMessageTone('error');
      setItemMessage(itemChangeErrorMessage(error));
    } finally {
      setItemChangeState('idle');
    }
  }

  async function confirmMedicineRemoval() {
    if (!removalTarget || itemChangeState !== 'idle') {
      return;
    }

    if (removalTarget.source === 'draft') {
      if (medicines.length <= 1) {
        setSubmissionState('failed');
        setMessage('Prescription must have at least one medicine');
      } else {
        setMedicines((current) =>
          current.filter((medicine) => medicine.clientId !== removalTarget.id),
        );
      }
      setRemovalTarget(null);
      return;
    }

    if (!savedPrescription) {
      setRemovalTarget(null);
      return;
    }

    if (savedPrescription.status === 'DISPENSED') {
      setItemMessageTone('error');
      setItemMessage('Cannot modify a dispensed prescription');
      setRemovalTarget(null);
      return;
    }

    if (savedPrescription.medicines.length <= 1) {
      setItemMessageTone('error');
      setItemMessage('Prescription must have at least one medicine');
      setRemovalTarget(null);
      return;
    }

    setItemChangeState('removing');
    setItemMessage(null);

    try {
      const prescription = await removePrescriptionMedicine(
        savedPrescription.id,
        removalTarget.id,
      );
      applyUpdatedPrescription(prescription);
      setItemMessageTone('success');
      setItemMessage('Medicine removed successfully.');
    } catch (error) {
      applyDispensedState(error);
      setItemMessageTone('error');
      setItemMessage(itemChangeErrorMessage(error));
    } finally {
      setItemChangeState('idle');
      setRemovalTarget(null);
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    if (!hasPrescriptionContext || submissionState === 'submitting') {
      return;
    }

    if (medicines.length === 0) {
      setSubmissionState('failed');
      setMessage('Add at least one medicine');
      return;
    }

    if (
      medicines.some(
        (medicine) =>
          !medicine.medicineName.trim()
          || !medicine.dosage.trim()
          || !medicine.frequency.trim()
          || !medicine.duration.trim(),
      )
    ) {
      setDraftValidated(true);
      setSubmissionState('failed');
      setMessage('Complete the medicine name, dosage, frequency, and duration');
      return;
    }

    setSubmissionState('submitting');
    setMessage(null);

    try {
      const prescription = await createPrescription({
        consultationId: context!.consultationId!,
        queueId: context!.queueId!,
        patientId: patientId!,
        medicines: medicines.map((medicine) => ({
          medicineName: medicine.medicineName.trim(),
          dosage: medicine.dosage.trim(),
          frequency: medicine.frequency.trim(),
          duration: medicine.duration.trim(),
          instructions: medicine.instructions.trim() || null,
        })),
      });

      setSavedPrescription(prescription);
      setHistory((current) => [
        prescription,
        ...current.filter((item) => item.id !== prescription.id),
      ]);
      setSubmissionState('saved');
      setMessage('Prescription saved successfully.');
    } catch (error) {
      setSubmissionState('failed');
      setMessage(
        error instanceof ApiError && (error.status === 401 || error.status === 403)
          ? 'You are not authorized to create prescriptions.'
          : error instanceof ApiError && error.status === 409
            ? error.message
            : 'Unable to save the prescription. Please try again.',
      );
    }
  }

  async function handleNoPrescriptionRequired() {
    if (!hasPrescriptionContext || noPrescriptionState === 'submitting') {
      return;
    }

    setNoPrescriptionState('submitting');
    setNoPrescriptionError(null);

    try {
      await recordNoPrescriptionRequired(context!.consultationId, {
        queueId: context!.queueId,
        patientId: context!.patientId,
      });
      void navigate('/doctor', { replace: true, state: { notice: 'No prescription required was recorded.' } });
    } catch (error) {
      setNoPrescriptionError(noPrescriptionErrorMessage(error));
      // Back to the confirm step, so the reason is shown beside the action that failed.
      setNoPrescriptionState('confirming');
    }
  }

  const isSubmitting = submissionState === 'submitting';
  const itemBusy = itemChangeState !== 'idle';

  // The Add Medicine button of the Medicines card: one for the unsaved draft, one for a
  // saved prescription that is still pending, and none once the visit has an outcome.
  function medicinesAction() {
    if (noPrescription) {
      return null;
    }

    if (submissionState !== 'saved') {
      return (
        <Button
          variant="secondary"
          size="sm"
          disabled={isSubmitting}
          onClick={() => setMedicines((current) => [...current, newMedicine()])}
        >
          Add Medicine
        </Button>
      );
    }

    if (savedPrescription?.status === 'PENDING' && !additionDraft) {
      return (
        <Button
          variant="secondary"
          size="sm"
          disabled={itemBusy}
          onClick={() => {
            setAdditionDraft(newMedicine());
            setAdditionValidated(false);
            setItemMessage(null);
          }}
        >
          Add Medicine
        </Button>
      );
    }

    return null;
  }

  function removalPanel(target: RemovalTarget) {
    return (
      <ConfirmPanel
        labelId={`remove-medicine-${target.id}`}
        title="Remove Medicine"
        confirmLabel="Confirm Remove"
        busyLabel="Removing…"
        busy={itemChangeState === 'removing'}
        onConfirm={() => void confirmMedicineRemoval()}
        onCancel={() => setRemovalTarget(null)}
        className="mt-4"
      >
        Remove {target.medicineName} from this prescription?
      </ConfirmPanel>
    );
  }

  function medicineFields(
    draft: MedicineDraft,
    showMissing: boolean,
    disabled: boolean,
    onChange: (field: keyof Omit<MedicineDraft, 'clientId'>, value: string) => void,
  ) {
    const missing = (value: string) => (showMissing && !value.trim() ? 'This field is required.' : null);

    return (
      <div className="grid gap-5 sm:grid-cols-2">
        {REQUIRED_MEDICINE_FIELDS.map(({ field, label, maxLength, hint }) => (
          <Field
            key={field}
            id={`${draft.clientId}-${field}`}
            label={label}
            required
            hint={hint}
            error={missing(draft[field])}
          >
            {(control) => (
              <input
                {...control}
                type="text"
                autoComplete="off"
                value={draft[field]}
                onChange={(event) => onChange(field, event.target.value)}
                maxLength={maxLength}
                disabled={disabled}
              />
            )}
          </Field>
        ))}
        <Field id={`${draft.clientId}-instructions`} label="Instructions" optional className="sm:col-span-2">
          {(control) => (
            <textarea
              {...control}
              value={draft.instructions}
              onChange={(event) => onChange('instructions', event.target.value)}
              maxLength={500}
              rows={2}
              disabled={disabled}
            />
          )}
        </Field>
      </div>
    );
  }

  return (
    <DashboardShell sectionLabel="Prescription" backLink={{ to: '/doctor', destination: 'Dashboard' }}>
      {context?.completed && (
        <Banner tone="success" title="Consultation Completed">
          Consultation completed successfully.
        </Banner>
      )}

      {/* A prescription reopened from the dashboard or after a reload has the patient's name
          but no queue number, so the banner shows whichever of the two it has. */}
      {(context?.queueNumber || context?.patientName) && (
        <Banner tone="info" title="Prescription For">
          <p className="break-words text-lg font-semibold text-slate-900" data-testid="prescription-context">
            {[context.queueNumber, context.patientName].filter(Boolean).join(' ')}
          </p>
        </Banner>
      )}

      {contextLoadState === 'loading' ? (
        <LoadingText>Recovering your pending prescription…</LoadingText>
      ) : contextLoadState === 'error' ? (
        <Banner tone="error" title="Prescription Unavailable" role="alert">
          Unable to check for a pending prescription. Please try again.
        </Banner>
      ) : !hasPrescriptionContext ? (
        <SectionCard title="No Prescription to Write">
          <p className="text-sm text-slate-700">
            Prescription details are unavailable. Complete a consultation before creating a prescription.
          </p>
          <ButtonLink to="/doctor" variant="secondary" className="mt-4">
            Go to Dashboard
          </ButtonLink>
        </SectionCard>
      ) : (
        <>
          <section className="space-y-3 empty:hidden" aria-label="Allergy warnings">
            {allergies.map((allergy) => (
              <AlertBanner key={allergy.allergyId} tone="allergy" label="Allergy Warning">
                WARNING: Patient is allergic to {allergy.allergyName} ({allergy.severity})
              </AlertBanner>
            ))}
          </section>

          {referenceLoadState === 'error' && (
            <Banner tone="warning" title="Some Information Is Missing" role="status">
              Some patient reference information could not be loaded. Reload this page to try again.
            </Banner>
          )}

          {/* The form and the patient's past prescriptions sit side by side on wide screens,
              so the doctor can check earlier medicines without leaving the form. */}
          <div className="grid items-start gap-6 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
          <SectionCard
            title="Medicines"
            description={
              submissionState === 'saved' || noPrescription
                ? undefined
                : 'Add every medicine included in this prescription, then save it.'
            }
            actions={medicinesAction()}
          >
            {noPrescription && (
              <div className="space-y-5" data-testid="no-prescription-recorded">
                <Banner tone="neutral" title="No Prescription Required" role="status">
                  Recorded by {noPrescription.doctorName} on {formatDate(noPrescription.createdAt)} at{' '}
                  {formatTime(noPrescription.createdAt)}. A prescription cannot be added for this consultation.
                </Banner>
                <ButtonLink to="/doctor">Back to Dashboard</ButtonLink>
              </div>
            )}

            {!noPrescription && submissionState !== 'saved' && (
              <form className="max-w-3xl space-y-5" noValidate onSubmit={handleSubmit}>
                {medicines.length === 0 ? (
                  <EmptyState>
                    <p>No medicines added. Select Add Medicine to start.</p>
                  </EmptyState>
                ) : (
                  <RequiredLegend />
                )}

                {medicines.map((medicine, index) => (
                  <fieldset key={medicine.clientId} className="rounded-md border border-slate-200 px-4 pb-4 pt-2">
                    <legend className="px-1.5 text-sm font-semibold text-slate-700">
                      Medicine {index + 1}
                    </legend>

                    {medicineFields(medicine, draftValidated, isSubmitting, (field, value) =>
                      updateMedicine(medicine.clientId, field, value),
                    )}

                    {removalTarget?.source === 'draft' && removalTarget.id === medicine.clientId ? (
                      removalPanel(removalTarget)
                    ) : (
                      <div className="mt-4">
                        <Button
                          variant="secondary"
                          size="sm"
                          disabled={isSubmitting}
                          onClick={() => setRemovalTarget({
                            source: 'draft',
                            id: medicine.clientId,
                            medicineName: medicine.medicineName.trim() || `Medicine ${index + 1}`,
                          })}
                        >
                          Remove
                        </Button>
                      </div>
                    )}
                  </fieldset>
                ))}

                {message && (
                  <Banner tone="error" role="alert">
                    {message}
                  </Banner>
                )}

                <Button type="submit" loading={isSubmitting}>
                  {isSubmitting ? 'Saving…' : 'Save Prescription'}
                </Button>
              </form>
            )}

            {/* The other way to finish this visit: record that no medicine is needed. */}
            {!noPrescription && submissionState !== 'saved' && (
              <div className="mt-6 max-w-3xl border-t border-slate-200 pt-5">
                {noPrescriptionState === 'idle' ? (
                  <>
                    <p className="text-sm text-slate-600">
                      If this patient needs no medicine, record that instead of writing a prescription.
                    </p>
                    <Button
                      variant="secondary"
                      size="sm"
                      className="mt-3"
                      disabled={isSubmitting}
                      onClick={() => setNoPrescriptionState('confirming')}
                      data-testid="no-prescription-required"
                    >
                      No prescription required
                    </Button>
                  </>
                ) : (
                  <ConfirmPanel
                    labelId="confirm-no-prescription-title"
                    title="No Prescription Required?"
                    tone="primary"
                    confirmLabel="Confirm"
                    busyLabel="Recording…"
                    busy={noPrescriptionState === 'submitting'}
                    error={noPrescriptionError}
                    onConfirm={() => void handleNoPrescriptionRequired()}
                    onCancel={() => {
                      setNoPrescriptionState('idle');
                      setNoPrescriptionError(null);
                    }}
                  >
                    This records that the patient needs no medicine. A prescription cannot be added for this
                    consultation afterwards.
                  </ConfirmPanel>
                )}
              </div>
            )}

            {savedPrescription && (
              <div className="space-y-5">
                {savedPrescription.status === 'DISPENSED' && (
                  <Banner tone="neutral" role="status">
                    Cannot modify a dispensed prescription
                  </Banner>
                )}

                <ul className="space-y-3" aria-label="Prescription medicines">
                  {savedPrescription.medicines.map((medicine) => {
                    const confirming =
                      removalTarget?.source === 'saved' && removalTarget.id === medicine.id;

                    return (
                      <li key={medicine.id} className="rounded-md border border-slate-200 p-4">
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div className="min-w-0 break-words text-sm text-slate-700">
                            <p className="font-semibold text-slate-900" data-testid="medicine-name">
                              {medicine.medicineName}
                            </p>
                            <p className="mt-1">
                              {medicine.dosage}, {medicine.frequency}, {medicine.duration}
                            </p>
                            {medicine.instructions && (
                              <p className="mt-1 text-slate-600">{medicine.instructions}</p>
                            )}
                          </div>
                          {savedPrescription.status === 'PENDING' && !confirming && (
                            <Button
                              variant="secondary"
                              size="sm"
                              disabled={itemBusy}
                              onClick={() => setRemovalTarget({
                                source: 'saved',
                                id: medicine.id,
                                medicineName: medicine.medicineName,
                              })}
                            >
                              Remove
                            </Button>
                          )}
                        </div>
                        {confirming && removalTarget && removalPanel(removalTarget)}
                      </li>
                    );
                  })}
                </ul>

                {additionDraft && savedPrescription.status === 'PENDING' && (
                  <form className="max-w-3xl rounded-md border border-slate-200 bg-slate-50 p-4" noValidate onSubmit={handleAddSavedMedicine}>
                    <h3 className="text-base font-semibold text-slate-900">
                      Add another medicine
                    </h3>
                    <div className="mt-2">
                      <RequiredLegend />
                    </div>
                    <div className="mt-4">
                      {medicineFields(additionDraft, additionValidated, itemBusy, updateAdditionDraft)}
                    </div>
                    <div className="mt-5 flex flex-wrap gap-3">
                      <Button type="submit" loading={itemChangeState === 'adding'} disabled={itemBusy}>
                        {itemChangeState === 'adding' ? 'Adding…' : 'Add Medicine'}
                      </Button>
                      <Button variant="secondary" disabled={itemBusy} onClick={() => setAdditionDraft(null)}>
                        Cancel
                      </Button>
                    </div>
                  </form>
                )}

                <div aria-live="polite" className="space-y-3 empty:hidden">
                  {message && (
                    <Banner tone="success" role="status">
                      {message}
                    </Banner>
                  )}

                  {itemMessage && (
                    <Banner
                      tone={itemMessageTone === 'success' ? 'success' : 'error'}
                      role={itemMessageTone === 'success' ? 'status' : 'alert'}
                    >
                      {itemMessage}
                    </Banner>
                  )}
                </div>

                <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 pt-4">
                  <ButtonLink to="/doctor" data-testid="back-to-dashboard">
                    Back to Dashboard
                  </ButtonLink>
                </div>
              </div>
            )}
          </SectionCard>

          <SectionCard title="Previous Prescriptions" description="Newest first. For reference while prescribing.">
            {referenceLoadState === 'loading' ? (
              <LoadingText>Loading prescription history…</LoadingText>
            ) : history.length === 0 ? (
              <EmptyState>
                <p>{NO_PRESCRIPTIONS_MESSAGE}</p>
              </EmptyState>
            ) : (
              <PrescriptionHistoryList prescriptions={history} currentPrescriptionId={savedPrescription?.id} />
            )}
          </SectionCard>
          </div>
        </>
      )}
    </DashboardShell>
  );
}
