import { useEffect, useState, type FormEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { getAllergies, type Allergy } from '../api/allergies';
import { ApiError } from '../api/client';
import {
  createPrescription,
  getPatientPrescriptions,
  type Prescription,
  type PrescriptionMedicineInput,
} from '../api/prescriptions';
import { AlertBanner } from '../components/AlertBanner';
import { DashboardShell } from '../dashboards/DashboardShell';
import { useAuth } from '../auth/useAuth';
import {
  findPendingPrescriptionContext,
  isPrescriptionContext,
  type PrescriptionContext,
} from '../prescriptions/pendingPrescription';

interface MedicineDraft extends Omit<PrescriptionMedicineInput, 'instructions'> {
  clientId: string;
  instructions: string;
}

type LoadState = 'loading' | 'loaded' | 'error';
type ContextLoadState = 'loading' | 'loaded' | 'error';
type SubmissionState = 'idle' | 'submitting' | 'saved' | 'failed';

const inputClassName =
  'mt-1.5 block w-full border-2 border-slate-400 bg-white px-3 py-2.5 text-sm text-slate-900 focus:border-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2';

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

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat('en-LK', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}

export function PrescriptionPage() {
  const { user } = useAuth();
  const location = useLocation();
  const navigatedContext = isPrescriptionContext(location.state) ? location.state : null;
  const [context, setContext] = useState<PrescriptionContext | null>(navigatedContext);
  const [contextLoadState, setContextLoadState] = useState<ContextLoadState>(
    navigatedContext ? 'loaded' : 'loading',
  );
  const patientId = context?.patientId;
  const [medicines, setMedicines] = useState<MedicineDraft[]>([]);
  const [allergies, setAllergies] = useState<Allergy[]>([]);
  const [history, setHistory] = useState<Prescription[]>([]);
  const [referenceLoadState, setReferenceLoadState] = useState<LoadState>('loading');
  const [submissionState, setSubmissionState] = useState<SubmissionState>('idle');
  const [message, setMessage] = useState<string | null>(null);
  const [savedPrescription, setSavedPrescription] = useState<Prescription | null>(null);

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
      const [allergyResult, historyResult] = await Promise.allSettled([
        getAllergies(patientId!),
        getPatientPrescriptions(patientId!),
      ]);

      if (disposed) {
        return;
      }

      if (allergyResult.status === 'fulfilled') {
        setAllergies(allergyResult.value);
      }

      if (historyResult.status === 'fulfilled') {
        setHistory(historyResult.value);
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
  }, [patientId]);

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

  function removeMedicine(clientId: string) {
    setMedicines((current) => current.filter((medicine) => medicine.clientId !== clientId));
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

  return (
    <DashboardShell sectionLabel="Prescription">
      <section className="mt-6 border-t-4 border-b border-brand-blue bg-blue-50 px-6 py-5">
        <h1 className="text-xl font-semibold text-slate-900">Prescription</h1>
        {context?.completed && (
          <p className="mt-2 text-sm font-semibold text-emerald-800">
            Consultation completed successfully.
          </p>
        )}
        {context?.queueNumber && (
          <p className="mt-2 text-sm text-slate-700">
            {context.queueNumber} {context.patientName}
          </p>
        )}
      </section>

      {contextLoadState === 'loading' ? (
        <p className="mt-6 text-sm text-slate-600">Recovering your pending prescription...</p>
      ) : contextLoadState === 'error' ? (
        <section className="mt-6 border border-red-300 bg-red-50 px-6 py-6" role="alert">
          <p className="text-sm text-red-900">
            Unable to check for a pending prescription. Please try again.
          </p>
        </section>
      ) : !hasPrescriptionContext ? (
        <section className="mt-6 border border-slate-300 bg-white px-6 py-6">
          <p className="text-sm text-slate-700">
            Prescription details are unavailable. Complete a consultation before creating
            a prescription.
          </p>
          <Link
            to="/doctor"
            className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
          >
            Back to Doctor Dashboard
          </Link>
        </section>
      ) : (
        <>
          <section className="mt-6 space-y-3" aria-label="Allergy warnings">
            {allergies.map((allergy) => (
              <AlertBanner key={allergy.allergyId} tone="allergy" label="Allergy Warning">
                ⚠️ WARNING: Patient is allergic to {allergy.allergyName} ({allergy.severity})
              </AlertBanner>
            ))}
          </section>

          {referenceLoadState === 'error' && (
            <p
              className="mt-4 border-l-4 border-amber-600 bg-amber-50 px-4 py-3 text-sm text-amber-900"
              role="status"
            >
              Some patient reference information could not be loaded. You can retry by
              reopening this page.
            </p>
          )}

          <section className="mt-6 border border-slate-300 bg-white px-6 py-6">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Medicines</h2>
                <p className="mt-1 text-sm text-slate-600">
                  Add every medicine included in this prescription.
                </p>
              </div>
              {submissionState !== 'saved' && (
                <button
                  type="button"
                  onClick={() => setMedicines((current) => [...current, newMedicine()])}
                  className="border-2 border-brand-blue px-4 py-2 text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:bg-blue-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
                >
                  Add Medicine
                </button>
              )}
            </div>

            <form className="mt-5 space-y-5" noValidate onSubmit={handleSubmit}>
              {medicines.length === 0 && submissionState !== 'saved' && (
                <p className="border border-dashed border-slate-400 px-4 py-5 text-sm text-slate-600">
                  No medicines added.
                </p>
              )}

              {medicines.map((medicine, index) => (
                <fieldset key={medicine.clientId} className="border border-slate-300 p-4">
                  <div className="flex items-center justify-between gap-3">
                    <legend className="px-1 text-sm font-bold text-slate-800">
                      Medicine {index + 1}
                    </legend>
                    <button
                      type="button"
                      onClick={() => removeMedicine(medicine.clientId)}
                      disabled={submissionState === 'submitting'}
                      className="text-xs font-bold uppercase tracking-[0.12em] text-red-700 hover:text-red-900 disabled:text-slate-400"
                    >
                      Remove
                    </button>
                  </div>

                  <div className="mt-3 grid gap-4 md:grid-cols-2">
                    <label className="text-sm font-semibold text-slate-700">
                      Medicine name
                      <input
                        value={medicine.medicineName}
                        onChange={(event) =>
                          updateMedicine(medicine.clientId, 'medicineName', event.target.value)
                        }
                        maxLength={200}
                        required
                        disabled={submissionState === 'submitting'}
                        className={inputClassName}
                      />
                    </label>
                    <label className="text-sm font-semibold text-slate-700">
                      Dosage
                      <input
                        value={medicine.dosage}
                        onChange={(event) =>
                          updateMedicine(medicine.clientId, 'dosage', event.target.value)
                        }
                        maxLength={100}
                        required
                        disabled={submissionState === 'submitting'}
                        className={inputClassName}
                      />
                    </label>
                    <label className="text-sm font-semibold text-slate-700">
                      Frequency
                      <input
                        value={medicine.frequency}
                        onChange={(event) =>
                          updateMedicine(medicine.clientId, 'frequency', event.target.value)
                        }
                        maxLength={100}
                        required
                        disabled={submissionState === 'submitting'}
                        className={inputClassName}
                      />
                    </label>
                    <label className="text-sm font-semibold text-slate-700">
                      Duration
                      <input
                        value={medicine.duration}
                        onChange={(event) =>
                          updateMedicine(medicine.clientId, 'duration', event.target.value)
                        }
                        maxLength={100}
                        required
                        disabled={submissionState === 'submitting'}
                        className={inputClassName}
                      />
                    </label>
                    <label className="text-sm font-semibold text-slate-700 md:col-span-2">
                      Instructions (optional)
                      <textarea
                        value={medicine.instructions}
                        onChange={(event) =>
                          updateMedicine(medicine.clientId, 'instructions', event.target.value)
                        }
                        maxLength={500}
                        rows={2}
                        disabled={submissionState === 'submitting'}
                        className={inputClassName}
                      />
                    </label>
                  </div>
                </fieldset>
              ))}

              {message && (
                <p
                  className={`border-l-4 px-4 py-3 text-sm ${
                    submissionState === 'saved'
                      ? 'border-emerald-700 bg-emerald-50 text-emerald-900'
                      : 'border-red-700 bg-red-50 text-red-900'
                  }`}
                  role={submissionState === 'saved' ? 'status' : 'alert'}
                >
                  {message}
                </p>
              )}

              {submissionState !== 'saved' ? (
                <button
                  type="submit"
                  disabled={submissionState === 'submitting'}
                  className="bg-brand-blue px-5 py-3 text-xs font-bold uppercase tracking-[0.12em] text-white hover:bg-brand-blue-dark disabled:cursor-not-allowed disabled:bg-slate-400"
                >
                  {submissionState === 'submitting' ? 'Saving…' : 'Save Prescription'}
                </button>
              ) : (
                <Link
                  to="/doctor"
                  className="inline-block bg-brand-blue px-5 py-3 text-xs font-bold uppercase tracking-[0.12em] text-white hover:bg-brand-blue-dark"
                >
                  Back to Doctor Dashboard
                </Link>
              )}
            </form>

            {savedPrescription && (
              <p className="mt-3 text-xs text-slate-500">
                Prescription reference: {savedPrescription.id}
              </p>
            )}
          </section>

          <section className="mt-6 border border-slate-300 bg-white px-6 py-6">
            <h2 className="text-lg font-semibold text-slate-900">Previous prescriptions</h2>
            {referenceLoadState === 'loading' ? (
              <p className="mt-3 text-sm text-slate-600">Loading prescription history…</p>
            ) : history.length === 0 ? (
              <p className="mt-3 text-sm text-slate-600">No previous prescriptions found.</p>
            ) : (
              <div className="mt-4 space-y-4">
                {history.map((prescription) => (
                  <article key={prescription.id} className="border border-slate-300 p-4">
                    <div className="flex flex-wrap justify-between gap-2 text-sm">
                      <p className="font-semibold text-slate-900">
                        {formatDateTime(prescription.createdAt)}
                      </p>
                      <p className="font-bold text-amber-800">{prescription.status}</p>
                    </div>
                    <p className="mt-1 text-sm text-slate-600">
                      Prescribed by {prescription.doctorName}
                    </p>
                    <ul className="mt-3 space-y-2">
                      {prescription.medicines.map((medicine) => (
                        <li
                          key={medicine.id}
                          className="border-l-4 border-brand-blue pl-3 text-sm text-slate-700"
                        >
                          <span className="font-semibold text-slate-900">
                            {medicine.medicineName}
                          </span>
                          {' — '}{medicine.dosage}, {medicine.frequency}, {medicine.duration}
                          {medicine.instructions && ` — ${medicine.instructions}`}
                        </li>
                      ))}
                    </ul>
                  </article>
                ))}
              </div>
            )}
          </section>
        </>
      )}
    </DashboardShell>
  );
}
