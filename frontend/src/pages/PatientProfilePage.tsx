import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';
import { getPatient } from '../api/patients';
import type { PatientProfile } from '../api/patients';
import { addAllergy, getAllergies, removeAllergy, updateAllergy } from '../api/allergies';
import type { Allergy, AllergyRequestBody, AllergySeverity } from '../api/allergies';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';

type LoadStatus = 'loading' | 'loaded' | 'notFound' | 'error';
type FormStatus = 'idle' | 'submitting' | 'failed';

interface AllergyFormState {
  allergyName: string;
  severity: AllergySeverity;
  notes: string;
}

interface AllergyFieldErrors {
  allergyName: string | null;
  severity: string | null;
}

const EMPTY_FIELD_ERRORS: AllergyFieldErrors = { allergyName: null, severity: null };
const EMPTY_FORM: AllergyFormState = { allergyName: '', severity: 'Severe', notes: '' };
const SEVERITY_OPTIONS: AllergySeverity[] = ['Severe', 'Moderate', 'Mild'];

const GENERIC_ERROR_MESSAGE = 'Something went wrong. Please try again.';
const FORBIDDEN_MANAGE_MESSAGE = 'You are not authorized to manage allergies.';

function inputClassName(hasError: boolean): string {
  return `mt-1.5 block w-full border-2 bg-white px-3 py-2.5 text-sm text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:bg-slate-100 disabled:text-slate-400 ${
    hasError ? 'border-red-600' : 'border-slate-400 focus:border-brand-blue'
  }`;
}

function severityBadgeClassName(severity: AllergySeverity): string {
  return severity === 'Severe'
    ? 'inline-block border border-red-700 bg-red-100 px-2 py-0.5 text-xs font-bold uppercase tracking-wider text-red-800'
    : 'inline-block border border-slate-400 bg-slate-100 px-2 py-0.5 text-xs font-bold uppercase tracking-wider text-slate-700';
}

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString();
}

function validateForm(form: AllergyFormState): AllergyFieldErrors {
  return {
    // Exact copy required by SWC-17 Scenario 2, mirroring AllergyRequest's server-side message.
    allergyName: form.allergyName.trim() ? null : 'Allergy name is required',
    severity: form.severity ? null : 'Severity is required.',
  };
}

export function PatientProfilePage() {
  const { patientId } = useParams<{ patientId: string }>();
  const { user } = useAuth();
  const backRoute = user ? roleRoutes[user.role] : '/login';
  const canManage = user?.role === 'Doctor' || user?.role === 'Receptionist';

  const [loadStatus, setLoadStatus] = useState<LoadStatus>('loading');
  const [patient, setPatient] = useState<PatientProfile | null>(null);
  const [allergies, setAllergies] = useState<Allergy[]>([]);

  const [addForm, setAddForm] = useState<AllergyFormState>(EMPTY_FORM);
  const [addFieldErrors, setAddFieldErrors] = useState<AllergyFieldErrors>(EMPTY_FIELD_ERRORS);
  const [addStatus, setAddStatus] = useState<FormStatus>('idle');
  const [addServerMessage, setAddServerMessage] = useState<string | null>(null);

  const [editingAllergyId, setEditingAllergyId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState<AllergyFormState>(EMPTY_FORM);
  const [editFieldErrors, setEditFieldErrors] = useState<AllergyFieldErrors>(EMPTY_FIELD_ERRORS);
  const [editStatus, setEditStatus] = useState<FormStatus>('idle');
  const [editServerMessage, setEditServerMessage] = useState<string | null>(null);

  const [confirmingRemovalId, setConfirmingRemovalId] = useState<string | null>(null);
  const [removeServerMessage, setRemoveServerMessage] = useState<string | null>(null);

  const latestRequestId = useRef(0);

  useEffect(() => {
    if (!patientId) {
      setLoadStatus('notFound');
      return;
    }

    const requestId = ++latestRequestId.current;
    setLoadStatus('loading');

    Promise.all([getPatient(patientId), getAllergies(patientId)])
      .then(([loadedPatient, loadedAllergies]) => {
        if (latestRequestId.current !== requestId) {
          return;
        }
        setPatient(loadedPatient);
        setAllergies(loadedAllergies);
        setLoadStatus('loaded');
      })
      .catch((error) => {
        if (latestRequestId.current !== requestId) {
          return;
        }
        if (error instanceof ApiError && error.status === 404) {
          setLoadStatus('notFound');
        } else {
          setLoadStatus('error');
        }
      });
  }, [patientId]);

  async function refetchAllergies() {
    if (!patientId) {
      return;
    }
    const refreshed = await getAllergies(patientId);
    setAllergies(refreshed);
  }

  async function handleAddSubmit(event: FormEvent) {
    event.preventDefault();
    if (!patientId) {
      return;
    }

    const errors = validateForm(addForm);
    setAddFieldErrors(errors);
    if (errors.allergyName || errors.severity) {
      return;
    }

    setAddStatus('submitting');
    setAddServerMessage(null);

    const request: AllergyRequestBody = {
      allergyName: addForm.allergyName.trim(),
      severity: addForm.severity,
      notes: addForm.notes.trim() || null,
    };

    try {
      await addAllergy(patientId, request);
      await refetchAllergies();
      setAddForm(EMPTY_FORM);
      setAddFieldErrors(EMPTY_FIELD_ERRORS);
      setAddStatus('idle');
    } catch (error) {
      setAddStatus('failed');
      if (error instanceof ApiError && error.status === 400 && Object.keys(error.fieldErrors).length > 0) {
        setAddFieldErrors((prev) => ({
          allergyName: error.fieldErrors.allergyname ?? prev.allergyName,
          severity: error.fieldErrors.severity ?? prev.severity,
        }));
      } else if (error instanceof ApiError && error.status === 403) {
        setAddServerMessage(FORBIDDEN_MANAGE_MESSAGE);
      } else {
        setAddServerMessage(GENERIC_ERROR_MESSAGE);
      }
    }
  }

  function startEdit(allergy: Allergy) {
    setEditingAllergyId(allergy.allergyId);
    setEditForm({ allergyName: allergy.allergyName, severity: allergy.severity, notes: allergy.notes ?? '' });
    setEditFieldErrors(EMPTY_FIELD_ERRORS);
    setEditStatus('idle');
    setEditServerMessage(null);
  }

  function cancelEdit() {
    setEditingAllergyId(null);
    setEditServerMessage(null);
  }

  async function handleEditSubmit(event: FormEvent, allergyId: string) {
    event.preventDefault();
    if (!patientId) {
      return;
    }

    const errors = validateForm(editForm);
    setEditFieldErrors(errors);
    if (errors.allergyName || errors.severity) {
      return;
    }

    setEditStatus('submitting');
    setEditServerMessage(null);

    const request: AllergyRequestBody = {
      allergyName: editForm.allergyName.trim(),
      severity: editForm.severity,
      notes: editForm.notes.trim() || null,
    };

    try {
      await updateAllergy(patientId, allergyId, request);
      await refetchAllergies();
      setEditingAllergyId(null);
      setEditStatus('idle');
    } catch (error) {
      setEditStatus('failed');
      if (error instanceof ApiError && error.status === 400 && Object.keys(error.fieldErrors).length > 0) {
        setEditFieldErrors((prev) => ({
          allergyName: error.fieldErrors.allergyname ?? prev.allergyName,
          severity: error.fieldErrors.severity ?? prev.severity,
        }));
      } else if (error instanceof ApiError && error.status === 403) {
        setEditServerMessage(FORBIDDEN_MANAGE_MESSAGE);
      } else {
        setEditServerMessage(GENERIC_ERROR_MESSAGE);
      }
    }
  }

  async function handleConfirmRemove(allergyId: string) {
    if (!patientId) {
      return;
    }

    setRemoveServerMessage(null);
    try {
      await removeAllergy(patientId, allergyId);
      await refetchAllergies();
      setConfirmingRemovalId(null);
    } catch (error) {
      setConfirmingRemovalId(null);
      if (error instanceof ApiError && error.status === 403) {
        setRemoveServerMessage(FORBIDDEN_MANAGE_MESSAGE);
      } else {
        setRemoveServerMessage(GENERIC_ERROR_MESSAGE);
      }
    }
  }

  return (
    <DashboardShell sectionLabel="Patient Profile">
      <Link
        to={backRoute}
        className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
      >
        ← Back to Dashboard
      </Link>

      {loadStatus === 'loading' && <p className="mt-6 text-sm text-slate-500">Loading patient…</p>}

      {loadStatus === 'notFound' && (
        <div className="mt-6 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3">
          <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">Patient Not Found</p>
          <p className="mt-1 text-sm text-red-900">No patient exists with this ID.</p>
        </div>
      )}

      {loadStatus === 'error' && (
        <div className="mt-6 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3">
          <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">Unable to Load Patient</p>
          <p className="mt-1 text-sm text-red-900">{GENERIC_ERROR_MESSAGE}</p>
        </div>
      )}

      {loadStatus === 'loaded' && patient && (
        <>
          <div className="mt-6 border border-slate-300 bg-white px-6 py-6">
            <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">Patient</p>
            <p className="mt-1 text-2xl font-semibold text-slate-900">{patient.fullName}</p>
            <dl className="mt-4 grid grid-cols-2 gap-x-6 gap-y-2 text-sm">
              <div>
                <dt className="text-xs font-bold uppercase tracking-widest text-slate-500">NIC</dt>
                <dd className="text-slate-800">{patient.nic}</dd>
              </div>
              <div>
                <dt className="text-xs font-bold uppercase tracking-widest text-slate-500">Phone</dt>
                <dd className="text-slate-800">{patient.phoneNumber}</dd>
              </div>
              <div>
                <dt className="text-xs font-bold uppercase tracking-widest text-slate-500">Blood Group</dt>
                <dd className="text-slate-800">{patient.bloodGroup}</dd>
              </div>
              <div>
                <dt className="text-xs font-bold uppercase tracking-widest text-slate-500">Date of Birth</dt>
                <dd className="text-slate-800">{formatDate(patient.dateOfBirth)}</dd>
              </div>
            </dl>
          </div>

          {/* Red alert banner: derives from the same allergies list the table renders, so
              it can never disagree with the table, and disappears by construction once
              the list is empty. */}
          {allergies.length > 0 && (
            <div className="mt-6 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3" role="alert">
              <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">Allergy Alert</p>
              <p className="mt-1 text-sm text-red-900">
                {allergies.map((allergy) => allergy.allergyName).join(', ')}
              </p>
            </div>
          )}

          <div className="mt-6 border border-slate-300 bg-white px-6 py-6">
            <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">Allergies</p>

            {allergies.length === 0 ? (
              <p className="mt-3 text-sm text-slate-500">No allergies recorded</p>
            ) : (
              <div className="mt-3 overflow-x-auto border border-slate-300">
                <table className="min-w-full divide-y divide-slate-200 text-sm">
                  <thead className="bg-slate-50">
                    <tr>
                      <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                        Allergy
                      </th>
                      <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                        Severity
                      </th>
                      <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                        Notes
                      </th>
                      <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                        Date Recorded
                      </th>
                      {canManage && (
                        <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                          Actions
                        </th>
                      )}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-200">
                    {allergies.map((allergy) =>
                      editingAllergyId === allergy.allergyId ? (
                        <tr key={allergy.allergyId}>
                          <td colSpan={canManage ? 5 : 4} className="px-4 py-3">
                            <form
                              onSubmit={(event) => handleEditSubmit(event, allergy.allergyId)}
                              noValidate
                              className="space-y-3"
                            >
                              {editServerMessage && (
                                <p className="border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                                  {editServerMessage}
                                </p>
                              )}
                              <div>
                                <label
                                  htmlFor={`edit-name-${allergy.allergyId}`}
                                  className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600"
                                >
                                  Allergy Name
                                </label>
                                <input
                                  id={`edit-name-${allergy.allergyId}`}
                                  type="text"
                                  value={editForm.allergyName}
                                  onChange={(event) => {
                                    setEditForm((prev) => ({ ...prev, allergyName: event.target.value }));
                                    setEditFieldErrors((prev) => ({ ...prev, allergyName: null }));
                                  }}
                                  disabled={editStatus === 'submitting'}
                                  aria-invalid={editFieldErrors.allergyName ? true : undefined}
                                  aria-describedby={editFieldErrors.allergyName ? `edit-name-error-${allergy.allergyId}` : undefined}
                                  className={inputClassName(!!editFieldErrors.allergyName)}
                                />
                                {editFieldErrors.allergyName && (
                                  <p
                                    id={`edit-name-error-${allergy.allergyId}`}
                                    className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700"
                                  >
                                    {editFieldErrors.allergyName}
                                  </p>
                                )}
                              </div>
                              <div>
                                <label
                                  htmlFor={`edit-severity-${allergy.allergyId}`}
                                  className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600"
                                >
                                  Severity
                                </label>
                                <select
                                  id={`edit-severity-${allergy.allergyId}`}
                                  value={editForm.severity}
                                  onChange={(event) =>
                                    setEditForm((prev) => ({ ...prev, severity: event.target.value as AllergySeverity }))
                                  }
                                  disabled={editStatus === 'submitting'}
                                  className={inputClassName(false)}
                                >
                                  {SEVERITY_OPTIONS.map((option) => (
                                    <option key={option} value={option}>
                                      {option}
                                    </option>
                                  ))}
                                </select>
                              </div>
                              <div>
                                <label
                                  htmlFor={`edit-notes-${allergy.allergyId}`}
                                  className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600"
                                >
                                  Notes
                                </label>
                                <textarea
                                  id={`edit-notes-${allergy.allergyId}`}
                                  rows={2}
                                  value={editForm.notes}
                                  onChange={(event) => setEditForm((prev) => ({ ...prev, notes: event.target.value }))}
                                  disabled={editStatus === 'submitting'}
                                  className={inputClassName(false)}
                                />
                              </div>
                              <div className="flex gap-3">
                                <button
                                  type="submit"
                                  disabled={editStatus === 'submitting'}
                                  className="bg-brand-blue px-4 py-2 text-xs font-bold uppercase tracking-[0.12em] text-white hover:bg-brand-blue-dark focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
                                >
                                  {editStatus === 'submitting' ? 'Saving…' : 'Save'}
                                </button>
                                <button
                                  type="button"
                                  onClick={cancelEdit}
                                  disabled={editStatus === 'submitting'}
                                  className="border-2 border-slate-400 px-4 py-2 text-xs font-bold uppercase tracking-[0.12em] text-slate-700 hover:border-brand-blue hover:text-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
                                >
                                  Cancel
                                </button>
                              </div>
                            </form>
                          </td>
                        </tr>
                      ) : (
                        <tr key={allergy.allergyId}>
                          <td className="px-4 py-2 text-slate-900">{allergy.allergyName}</td>
                          <td className="px-4 py-2">
                            <span className={severityBadgeClassName(allergy.severity)}>{allergy.severity}</span>
                          </td>
                          <td className="px-4 py-2 text-slate-700">{allergy.notes ?? '—'}</td>
                          <td className="px-4 py-2 text-slate-700">{formatDate(allergy.recordedAt)}</td>
                          {canManage && (
                            <td className="px-4 py-2">
                              {confirmingRemovalId === allergy.allergyId ? (
                                <div className="flex flex-col gap-2">
                                  <p className="text-xs font-medium text-red-700">
                                    Are you sure you want to remove this allergy?
                                  </p>
                                  {removeServerMessage && (
                                    <p className="text-xs font-medium text-red-700">{removeServerMessage}</p>
                                  )}
                                  <div className="flex gap-2">
                                    <button
                                      type="button"
                                      onClick={() => handleConfirmRemove(allergy.allergyId)}
                                      className="border-2 border-red-700 bg-red-700 px-3 py-1.5 text-xs font-bold uppercase tracking-[0.1em] text-white hover:bg-red-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-red-700 focus-visible:ring-offset-2"
                                    >
                                      Confirm
                                    </button>
                                    <button
                                      type="button"
                                      onClick={() => setConfirmingRemovalId(null)}
                                      className="border-2 border-slate-400 px-3 py-1.5 text-xs font-bold uppercase tracking-[0.1em] text-slate-700 hover:border-brand-blue hover:text-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
                                    >
                                      Cancel
                                    </button>
                                  </div>
                                </div>
                              ) : (
                                <div className="flex gap-2">
                                  <button
                                    type="button"
                                    onClick={() => startEdit(allergy)}
                                    className="border-2 border-slate-400 px-3 py-1.5 text-xs font-bold uppercase tracking-[0.1em] text-slate-700 hover:border-brand-blue hover:text-brand-blue focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
                                  >
                                    Edit
                                  </button>
                                  <button
                                    type="button"
                                    onClick={() => {
                                      setRemoveServerMessage(null);
                                      setConfirmingRemovalId(allergy.allergyId);
                                    }}
                                    className="border-2 border-slate-400 px-3 py-1.5 text-xs font-bold uppercase tracking-[0.1em] text-slate-700 hover:border-red-700 hover:text-red-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
                                  >
                                    Remove
                                  </button>
                                </div>
                              )}
                            </td>
                          )}
                        </tr>
                      ),
                    )}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {canManage && (
            <div className="mt-6 border border-slate-300 bg-white px-6 py-6">
              <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">Add Allergy</p>

              <div aria-live="polite">
                {addStatus === 'failed' && addServerMessage && (
                  <div className="mt-3 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3">
                    <p className="text-sm text-red-900">{addServerMessage}</p>
                  </div>
                )}
              </div>

              <form onSubmit={handleAddSubmit} noValidate className="mt-3 space-y-4">
                <div>
                  <label htmlFor="add-allergy-name" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                    Allergy Name
                  </label>
                  <input
                    id="add-allergy-name"
                    type="text"
                    autoComplete="off"
                    value={addForm.allergyName}
                    onChange={(event) => {
                      setAddForm((prev) => ({ ...prev, allergyName: event.target.value }));
                      setAddFieldErrors((prev) => ({ ...prev, allergyName: null }));
                    }}
                    disabled={addStatus === 'submitting'}
                    aria-invalid={addFieldErrors.allergyName ? true : undefined}
                    aria-describedby={addFieldErrors.allergyName ? 'add-allergy-name-error' : undefined}
                    className={inputClassName(!!addFieldErrors.allergyName)}
                  />
                  {addFieldErrors.allergyName && (
                    <p id="add-allergy-name-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                      {addFieldErrors.allergyName}
                    </p>
                  )}
                </div>

                <div>
                  <label htmlFor="add-severity" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                    Severity
                  </label>
                  <select
                    id="add-severity"
                    value={addForm.severity}
                    onChange={(event) => setAddForm((prev) => ({ ...prev, severity: event.target.value as AllergySeverity }))}
                    disabled={addStatus === 'submitting'}
                    className={inputClassName(false)}
                  >
                    {SEVERITY_OPTIONS.map((option) => (
                      <option key={option} value={option}>
                        {option}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label htmlFor="add-notes" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
                    Notes
                  </label>
                  <textarea
                    id="add-notes"
                    rows={2}
                    value={addForm.notes}
                    onChange={(event) => setAddForm((prev) => ({ ...prev, notes: event.target.value }))}
                    disabled={addStatus === 'submitting'}
                    className={inputClassName(false)}
                  />
                </div>

                <button
                  type="submit"
                  disabled={addStatus === 'submitting'}
                  className="bg-brand-blue px-4 py-3 text-sm font-bold uppercase tracking-[0.15em] text-white hover:bg-brand-blue-dark focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
                >
                  {addStatus === 'submitting' ? 'Saving…' : 'Add Allergy'}
                </button>
              </form>
            </div>
          )}
        </>
      )}
    </DashboardShell>
  );
}
