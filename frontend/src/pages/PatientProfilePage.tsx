import { Fragment, useEffect, useRef, useState, type FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';
import { checkInPatient, getPatient, updatePatient } from '../api/patients';
import type { BloodGroup, PatientProfile, UpdatePatientRequestBody } from '../api/patients';
import { getPatientQueueStatus } from '../api/queue';
import type { PatientQueueStatus } from '../api/queue';
import { addAllergy, getAllergies, removeAllergy, updateAllergy } from '../api/allergies';
import type { Allergy, AllergyRequestBody, AllergySeverity } from '../api/allergies';
import { addCondition, getConditions, removeCondition } from '../api/conditions';
import type { ChronicCondition, ChronicConditionRequestBody } from '../api/conditions';
import { ApiError } from '../api/client';
import { getLatestOverdueFollowUp } from '../api/consultations';
import type { OverdueFollowUp } from '../api/consultations';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';
import { AlertBanner } from '../components/AlertBanner';
import { Banner } from '../components/ui/Banner';
import { Button, ButtonLink } from '../components/ui/Button';
import { ConfirmPanel } from '../components/ui/ConfirmPanel';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { Field, RequiredLegend } from '../components/ui/Field';
import { SectionCard } from '../components/ui/SectionCard';
import { StatusBadge, type StatusBadgeTone } from '../components/ui/StatusBadge';
import {
  tableBodyClassName,
  tableCellClassName,
  tableClassName,
  tableHeadClassName,
  tableHeaderCellClassName,
  tableKeyCellClassName,
  tableWrapperClassName,
} from '../components/ui/table';
import { clinicTodayForDateInput, formatDate } from '../lib/format';

type LoadStatus = 'loading' | 'loaded' | 'notFound' | 'error';
type FormStatus = 'idle' | 'submitting' | 'failed';
type QueueStatusLoadState = 'idle' | 'loading' | 'loaded' | 'error';
type AllergiesLoadState = 'loading' | 'loaded' | 'error';
type ConditionsLoadState = 'loading' | 'loaded' | 'error';
type ProfileUpdateStatus = 'idle' | 'submitting' | 'saved' | 'failed';
type CheckInStatus = 'idle' | 'submitting' | 'awaitingQueue' | 'succeeded' | 'accepted' | 'failed';

interface ProfileEditFormState {
  address: string;
  phoneNumber: string;
  bloodGroup: BloodGroup;
}

interface ProfileFieldErrors {
  address: string | null;
  phoneNumber: string | null;
  bloodGroup: string | null;
}

interface AllergyFormState {
  allergyName: string;
  severity: AllergySeverity;
  notes: string;
}

interface AllergyFieldErrors {
  allergyName: string | null;
  severity: string | null;
}

interface ConditionFormState {
  conditionName: string;
  dateDiagnosed: string;
  notes: string;
}

interface ConditionFieldErrors {
  conditionName: string | null;
  dateDiagnosed: string | null;
}

const EMPTY_FIELD_ERRORS: AllergyFieldErrors = { allergyName: null, severity: null };
const EMPTY_FORM: AllergyFormState = { allergyName: '', severity: 'Severe', notes: '' };
const SEVERITY_OPTIONS: AllergySeverity[] = ['Severe', 'Moderate', 'Mild'];
const EMPTY_CONDITION_FORM: ConditionFormState = {
  conditionName: '',
  dateDiagnosed: '',
  notes: '',
};
const EMPTY_CONDITION_FIELD_ERRORS: ConditionFieldErrors = {
  conditionName: null,
  dateDiagnosed: null,
};
const EMPTY_PROFILE_FIELD_ERRORS: ProfileFieldErrors = {
  address: null,
  phoneNumber: null,
  bloodGroup: null,
};
const BLOOD_GROUP_OPTIONS: BloodGroup[] = ['A+', 'A-', 'B+', 'B-', 'O+', 'O-', 'AB+', 'AB-'];
const PHONE_PATTERN = /^(0[0-9]{9}|\+94[0-9]{9})$/;
const QUEUE_ASSIGNMENT_MAX_ATTEMPTS = 20;
const QUEUE_ASSIGNMENT_RETRY_DELAY_MS = 500;

const GENERIC_ERROR_MESSAGE = 'Something went wrong. Please try again.';
const FORBIDDEN_MANAGE_MESSAGE = 'You are not authorized to manage allergies.';

const SEVERITY_TONES: Record<AllergySeverity, StatusBadgeTone> = {
  Severe: 'danger',
  Moderate: 'warning',
  Mild: 'neutral',
};

const SUB_HEADING_CLASS_NAME = 'text-sm font-bold uppercase tracking-[0.12em] text-slate-700';
const DETAIL_TERM_CLASS_NAME = 'text-xs font-bold uppercase tracking-[0.12em] text-slate-500';

function formatMonthYear(value: string): string {
  const [year, month] = value.slice(0, 10).split('-').map(Number);
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    year: 'numeric',
  }).format(new Date(year, month - 1, 1));
}

function calculateAge(dateOfBirth: string): number {
  const [year, month, day] = dateOfBirth.slice(0, 10).split('-').map(Number);
  const today = new Date();
  let age = today.getFullYear() - year;
  const birthdayHasPassed =
    today.getMonth() + 1 > month ||
    (today.getMonth() + 1 === month && today.getDate() >= day);

  if (!birthdayHasPassed) {
    age -= 1;
  }

  return age;
}

function wait(delayMilliseconds: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, delayMilliseconds));
}

async function waitForQueueAssignment(patientId: string): Promise<PatientQueueStatus | null> {
  let successfulLookup = false;
  let lastLookupError: unknown = null;

  for (let attempt = 0; attempt < QUEUE_ASSIGNMENT_MAX_ATTEMPTS; attempt += 1) {
    if (attempt > 0) {
      await wait(QUEUE_ASSIGNMENT_RETRY_DELAY_MS);
    }

    try {
      const status = await getPatientQueueStatus(patientId);
      successfulLookup = true;
      lastLookupError = null;
      if (status.isCheckedIn && status.queueNumber) {
        return status;
      }
    } catch (error) {
      if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
        throw error;
      }

      lastLookupError = error;
      // Queue creation is asynchronous. A transient lookup failure is retried within
      // this bounded post-check-in window rather than starting permanent page polling.
    }
  }

  if (!successfulLookup && lastLookupError) {
    throw lastLookupError;
  }

  return null;
}

function validateProfileForm(form: ProfileEditFormState): ProfileFieldErrors {
  const address = form.address.trim();
  const phoneNumber = form.phoneNumber.trim();

  return {
    address: address
      ? address.length <= 256
        ? null
        : 'Address must be 256 characters or fewer.'
      : 'Address is required.',
    phoneNumber: phoneNumber
      ? PHONE_PATTERN.test(phoneNumber)
        ? null
        : 'Phone number must be a valid Sri Lankan number.'
      : 'Phone number is required.',
    bloodGroup: form.bloodGroup ? null : 'Blood group is required.',
  };
}

function validateForm(form: AllergyFormState): AllergyFieldErrors {
  return {
    // Exact copy required by SWC-17 Scenario 2, mirroring AllergyRequest's server-side message.
    allergyName: form.allergyName.trim() ? null : 'Allergy name is required',
    severity: form.severity ? null : 'Severity is required.',
  };
}

function validateConditionForm(form: ConditionFormState): ConditionFieldErrors {
  return {
    conditionName: form.conditionName.trim() ? null : 'Condition name is required',
    dateDiagnosed: !form.dateDiagnosed
      ? 'Diagnosed date is required.'
      : form.dateDiagnosed > clinicTodayForDateInput()
        ? 'Diagnosed date cannot be in the future'
        : null,
  };
}

export function PatientProfilePage() {
  const { patientId } = useParams<{ patientId: string }>();
  const { user } = useAuth();
  const backRoute = user ? roleRoutes[user.role] : '/login';
  const canManage = user?.role === 'Doctor' || user?.role === 'Receptionist';
  const isReceptionist = user?.role === 'Receptionist';

  const [loadStatus, setLoadStatus] = useState<LoadStatus>('loading');
  const [patient, setPatient] = useState<PatientProfile | null>(null);
  const [allergies, setAllergies] = useState<Allergy[]>([]);
  const [allergiesLoadState, setAllergiesLoadState] = useState<AllergiesLoadState>('loading');
  const [conditions, setConditions] = useState<ChronicCondition[]>([]);
  const [conditionsLoadState, setConditionsLoadState] = useState<ConditionsLoadState>('loading');
  const [overdueFollowUp, setOverdueFollowUp] = useState<OverdueFollowUp | null>(null);
  const [followUpLoadFailed, setFollowUpLoadFailed] = useState(false);
  const [queueStatus, setQueueStatus] = useState<PatientQueueStatus | null>(null);
  const [queueStatusLoadState, setQueueStatusLoadState] = useState<QueueStatusLoadState>('idle');
  const [checkInStatus, setCheckInStatus] = useState<CheckInStatus>('idle');
  const [checkInMessage, setCheckInMessage] = useState<string | null>(null);

  const [isEditingProfile, setIsEditingProfile] = useState(false);
  const [profileForm, setProfileForm] = useState<ProfileEditFormState | null>(null);
  const [profileFieldErrors, setProfileFieldErrors] = useState<ProfileFieldErrors>(EMPTY_PROFILE_FIELD_ERRORS);
  const [profileUpdateStatus, setProfileUpdateStatus] = useState<ProfileUpdateStatus>('idle');
  const [profileServerMessage, setProfileServerMessage] = useState<string | null>(null);

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
  const [isRemovingAllergy, setIsRemovingAllergy] = useState(false);
  // A save that worked but whose follow-up reload failed is not a failed save, so it
  // gets its own message instead of being reported through the form's error.
  const [allergiesRefreshFailed, setAllergiesRefreshFailed] = useState(false);

  const [conditionForm, setConditionForm] = useState<ConditionFormState>(EMPTY_CONDITION_FORM);
  const [conditionFieldErrors, setConditionFieldErrors] = useState<ConditionFieldErrors>(
    EMPTY_CONDITION_FIELD_ERRORS,
  );
  const [conditionAddStatus, setConditionAddStatus] = useState<FormStatus>('idle');
  const [conditionAddServerMessage, setConditionAddServerMessage] = useState<string | null>(null);
  const [confirmingConditionRemovalId, setConfirmingConditionRemovalId] = useState<string | null>(null);
  const [conditionRemoveServerMessage, setConditionRemoveServerMessage] = useState<string | null>(null);
  const [isRemovingCondition, setIsRemovingCondition] = useState(false);

  const latestRequestId = useRef(0);

  useEffect(() => {
    if (!patientId) {
      setLoadStatus('notFound');
      return;
    }

    const requestId = ++latestRequestId.current;
    setLoadStatus('loading');
    setAllergies([]);
    setAllergiesLoadState('loading');
    setConditions([]);
    setConditionsLoadState('loading');
    setOverdueFollowUp(null);
    setFollowUpLoadFailed(false);
    setQueueStatus(null);
    setQueueStatusLoadState(isReceptionist ? 'loading' : 'idle');
    setCheckInStatus('idle');
    setCheckInMessage(null);

    const queueStatusRequest = isReceptionist
      ? getPatientQueueStatus(patientId)
          .then((status) => ({ status, failed: false as const }))
          .catch(() => ({ status: null, failed: true as const }))
      : Promise.resolve({ status: null, failed: false as const });

    const conditionsRequest = getConditions(patientId)
      .then((items) => ({ items, failed: false as const }))
      .catch(() => ({ items: [], failed: true as const }));

    const allergiesRequest = getAllergies(patientId)
      .then((items) => ({ items, failed: false as const }))
      .catch(() => ({ items: [], failed: true as const }));

    const followUpRequest = user?.role === 'Doctor'
      ? getLatestOverdueFollowUp(patientId)
          .then((followUp) => ({ followUp: followUp ?? null, failed: false as const }))
          .catch(() => ({ followUp: null, failed: true as const }))
      : Promise.resolve({ followUp: null, failed: false as const });

    Promise.all([
      getPatient(patientId),
      allergiesRequest,
      conditionsRequest,
      queueStatusRequest,
      followUpRequest,
    ])
      .then(([
        loadedPatient,
        loadedAllergies,
        loadedConditions,
        loadedQueueStatus,
        loadedFollowUp,
      ]) => {
        if (latestRequestId.current !== requestId) {
          return;
        }
        setPatient(loadedPatient);
        setAllergies(loadedAllergies.items);
        setAllergiesLoadState(loadedAllergies.failed ? 'error' : 'loaded');
        setConditions(loadedConditions.items);
        setConditionsLoadState(loadedConditions.failed ? 'error' : 'loaded');
        setOverdueFollowUp(loadedFollowUp.followUp);
        setFollowUpLoadFailed(loadedFollowUp.failed);
        setProfileForm({
          address: loadedPatient.address,
          phoneNumber: loadedPatient.phoneNumber,
          bloodGroup: loadedPatient.bloodGroup,
        });
        setQueueStatus(loadedQueueStatus.status);
        setQueueStatusLoadState(
          isReceptionist ? (loadedQueueStatus.failed ? 'error' : 'loaded') : 'idle',
        );
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
  }, [isReceptionist, patientId, user?.role]);

  async function handleCheckIn() {
    if (!patientId || !patient || !isReceptionist) {
      return;
    }

    const requestId = latestRequestId.current;
    setCheckInStatus('submitting');
    setCheckInMessage(null);

    try {
      await checkInPatient(patientId);
    } catch (error) {
      if (latestRequestId.current !== requestId) {
        return;
      }

      setCheckInStatus('failed');
      setCheckInMessage(
        error instanceof ApiError
          ? error.message
          : 'Unable to check in the patient. Please try again.',
      );
      return;
    }

    if (latestRequestId.current !== requestId) {
      return;
    }

    setCheckInStatus('awaitingQueue');

    try {
      const assignedStatus = await waitForQueueAssignment(patientId);
      if (latestRequestId.current !== requestId) {
        return;
      }

      if (!assignedStatus) {
        setCheckInStatus('accepted');
        setCheckInMessage(
          'Check-in accepted. The queue number is still being assigned. Refresh the profile to check again.',
        );
        return;
      }

      setQueueStatus(assignedStatus);
      setQueueStatusLoadState('loaded');
      setCheckInStatus('succeeded');
    } catch (error) {
      if (latestRequestId.current !== requestId) {
        return;
      }

      setCheckInStatus('accepted');
      setQueueStatusLoadState('error');
      setCheckInMessage(
        error instanceof ApiError && (error.status === 401 || error.status === 403)
          ? 'Check-in was accepted, but the assigned queue number could not be retrieved. Please sign in again.'
          : 'Check-in was accepted, but the assigned queue number could not be retrieved. Refresh the profile to check again.',
      );
    }
  }

  function startProfileEdit() {
    if (!patient) {
      return;
    }

    setProfileForm({
      address: patient.address,
      phoneNumber: patient.phoneNumber,
      bloodGroup: patient.bloodGroup,
    });
    setProfileFieldErrors(EMPTY_PROFILE_FIELD_ERRORS);
    setProfileUpdateStatus('idle');
    setProfileServerMessage(null);
    setIsEditingProfile(true);
  }

  function cancelProfileEdit() {
    setIsEditingProfile(false);
    setProfileFieldErrors(EMPTY_PROFILE_FIELD_ERRORS);
    setProfileUpdateStatus('idle');
    setProfileServerMessage(null);
  }

  function clearProfileFieldError(field: keyof ProfileFieldErrors) {
    setProfileFieldErrors((previous) =>
      previous[field] ? { ...previous, [field]: null } : previous,
    );
    if (profileUpdateStatus === 'failed') {
      setProfileUpdateStatus('idle');
      setProfileServerMessage(null);
    }
  }

  async function handleProfileUpdate(event: FormEvent) {
    event.preventDefault();
    if (!patientId || !profileForm) {
      return;
    }

    const errors = validateProfileForm(profileForm);
    setProfileFieldErrors(errors);
    if (Object.values(errors).some((error) => error !== null)) {
      return;
    }

    setProfileUpdateStatus('submitting');
    setProfileServerMessage(null);

    const request: UpdatePatientRequestBody = {
      address: profileForm.address.trim(),
      phoneNumber: profileForm.phoneNumber.trim(),
      bloodGroup: profileForm.bloodGroup,
    };

    try {
      const updatedPatient = await updatePatient(patientId, request);
      setPatient(updatedPatient);
      setProfileForm({
        address: updatedPatient.address,
        phoneNumber: updatedPatient.phoneNumber,
        bloodGroup: updatedPatient.bloodGroup,
      });
      setProfileFieldErrors(EMPTY_PROFILE_FIELD_ERRORS);
      setProfileUpdateStatus('saved');
      setIsEditingProfile(false);
    } catch (error) {
      setProfileUpdateStatus('failed');
      if (error instanceof ApiError && error.status === 400 && Object.keys(error.fieldErrors).length > 0) {
        setProfileFieldErrors((previous) => ({
          address: error.fieldErrors.address ?? previous.address,
          phoneNumber: error.fieldErrors.phonenumber ?? previous.phoneNumber,
          bloodGroup: error.fieldErrors.bloodgroup ?? previous.bloodGroup,
        }));
      } else if (error instanceof ApiError && error.status === 403) {
        setProfileServerMessage('You are not authorized to update patient profiles.');
      } else {
        setProfileServerMessage('Unable to update the patient profile. Please try again.');
      }
    }
  }

  // Never throws: the caller has already saved successfully by the time this runs.
  async function refetchAllergies() {
    if (!patientId) {
      return;
    }
    try {
      const refreshed = await getAllergies(patientId);
      setAllergies(refreshed);
      setAllergiesRefreshFailed(false);
    } catch {
      setAllergiesRefreshFailed(true);
    }
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
    setIsRemovingAllergy(true);
    try {
      await removeAllergy(patientId, allergyId);
      await refetchAllergies();
      setConfirmingRemovalId(null);
    } catch (error) {
      // The confirm step stays open so the reason is shown next to the allergy it is about.
      if (error instanceof ApiError && error.status === 403) {
        setRemoveServerMessage(FORBIDDEN_MANAGE_MESSAGE);
      } else {
        setRemoveServerMessage(GENERIC_ERROR_MESSAGE);
      }
    } finally {
      setIsRemovingAllergy(false);
    }
  }

  async function handleConditionAddSubmit(event: FormEvent) {
    event.preventDefault();
    if (!patientId || !isReceptionist) {
      return;
    }

    const errors = validateConditionForm(conditionForm);
    setConditionFieldErrors(errors);
    if (errors.conditionName || errors.dateDiagnosed) {
      return;
    }

    setConditionAddStatus('submitting');
    setConditionAddServerMessage(null);

    const request: ChronicConditionRequestBody = {
      conditionName: conditionForm.conditionName.trim(),
      dateDiagnosed: conditionForm.dateDiagnosed,
      notes: conditionForm.notes.trim() || null,
    };

    try {
      const addedCondition = await addCondition(patientId, request);
      setConditions((previous) =>
        [...previous, addedCondition].sort(
          (left, right) =>
            right.dateDiagnosed.localeCompare(left.dateDiagnosed) ||
            left.conditionName.localeCompare(right.conditionName),
        ),
      );
      setConditionForm(EMPTY_CONDITION_FORM);
      setConditionFieldErrors(EMPTY_CONDITION_FIELD_ERRORS);
      setConditionAddStatus('idle');
    } catch (error) {
      setConditionAddStatus('failed');
      if (error instanceof ApiError && error.status === 400 && Object.keys(error.fieldErrors).length > 0) {
        setConditionFieldErrors((previous) => ({
          conditionName: error.fieldErrors.conditionname ?? previous.conditionName,
          dateDiagnosed: error.fieldErrors.datediagnosed ?? previous.dateDiagnosed,
        }));
      } else if (error instanceof ApiError && error.status === 403) {
        setConditionAddServerMessage('You are not authorized to manage chronic conditions.');
      } else {
        setConditionAddServerMessage(GENERIC_ERROR_MESSAGE);
      }
    }
  }

  async function handleConfirmConditionRemove(conditionId: string) {
    if (!patientId || !isReceptionist) {
      return;
    }

    setConditionRemoveServerMessage(null);
    setIsRemovingCondition(true);
    try {
      await removeCondition(patientId, conditionId);
      setConditions((previous) =>
        previous.filter((condition) => condition.conditionId !== conditionId),
      );
      setConfirmingConditionRemovalId(null);
    } catch (error) {
      setConditionRemoveServerMessage(
        error instanceof ApiError && error.status === 403
          ? 'You are not authorized to manage chronic conditions.'
          : GENERIC_ERROR_MESSAGE,
      );
    } finally {
      setIsRemovingCondition(false);
    }
  }

  const isSavingProfile = profileUpdateStatus === 'submitting';
  const isCheckingIn = checkInStatus === 'submitting' || checkInStatus === 'awaitingQueue';
  const allergyColumnCount = canManage ? 5 : 4;
  const conditionColumnCount = isReceptionist ? 4 : 3;

  return (
    <DashboardShell sectionLabel="Patient Profile" backLink={{ to: backRoute, destination: 'Dashboard' }}>
      {loadStatus === 'loading' && <LoadingText>Loading patient…</LoadingText>}

      {loadStatus === 'notFound' && (
        <Banner tone="error" title="Patient Not Found">
          No patient exists with this ID.
        </Banner>
      )}

      {loadStatus === 'error' && (
        <Banner tone="error" title="Unable to Load Patient">
          {GENERIC_ERROR_MESSAGE}
        </Banner>
      )}

      {loadStatus === 'loaded' && patient && (
        <>
          <div aria-live="polite" className="empty:hidden">
            {profileUpdateStatus === 'saved' && (
              <Banner tone="success" title="Profile Updated">
                Patient profile updated successfully.
              </Banner>
            )}
          </div>

          <SectionCard
            eyebrow="Patient"
            title={patient.fullName}
            actions={
              <>
                {user?.role === 'Doctor' && (
                  <ButtonLink to={`/patients/${patient.patientId}/history`} variant="secondary" size="sm">
                    View History
                  </ButtonLink>
                )}
                {isReceptionist && !isEditingProfile && (
                  <Button variant="secondary" size="sm" onClick={startProfileEdit}>
                    Edit Profile
                  </Button>
                )}
              </>
            }
          >
            <dl className="grid gap-x-6 gap-y-4 text-sm sm:grid-cols-2 lg:grid-cols-3">
              <div>
                <dt className={DETAIL_TERM_CLASS_NAME}>Patient ID</dt>
                <dd className="mt-0.5 break-all text-slate-900">{patient.patientId}</dd>
              </div>
              <div>
                <dt className={DETAIL_TERM_CLASS_NAME}>NIC</dt>
                <dd className="mt-0.5 text-slate-900">{patient.nic}</dd>
              </div>
              <div>
                <dt className={DETAIL_TERM_CLASS_NAME}>Date of Birth</dt>
                <dd className="mt-0.5 text-slate-900">{formatDate(patient.dateOfBirth.slice(0, 10))}</dd>
              </div>
              <div>
                <dt className={DETAIL_TERM_CLASS_NAME}>Age</dt>
                <dd className="mt-0.5 text-slate-900">{calculateAge(patient.dateOfBirth)}</dd>
              </div>
              <div>
                <dt className={DETAIL_TERM_CLASS_NAME}>Gender</dt>
                <dd className="mt-0.5 text-slate-900">{patient.gender}</dd>
              </div>
              <div>
                <dt className={DETAIL_TERM_CLASS_NAME}>Registration Date</dt>
                <dd className="mt-0.5 text-slate-900">{formatDate(patient.registeredAt)}</dd>
              </div>
              {/* While editing, the form below stands in for these three values. */}
              {!isEditingProfile && (
                <>
                  <div>
                    <dt className={DETAIL_TERM_CLASS_NAME}>Phone</dt>
                    <dd className="mt-0.5 text-slate-900">{patient.phoneNumber}</dd>
                  </div>
                  <div>
                    <dt className={DETAIL_TERM_CLASS_NAME}>Blood Group</dt>
                    <dd className="mt-0.5 text-slate-900">{patient.bloodGroup}</dd>
                  </div>
                  <div>
                    <dt className={DETAIL_TERM_CLASS_NAME}>Address</dt>
                    <dd className="mt-0.5 whitespace-pre-wrap break-words text-slate-900">{patient.address}</dd>
                  </div>
                </>
              )}
            </dl>

            {isReceptionist && isEditingProfile && profileForm && (
              <form onSubmit={handleProfileUpdate} noValidate className="mt-6 border-t border-slate-200 pt-5">
                <h3 className={SUB_HEADING_CLASS_NAME}>Edit Contact Details</h3>
                <p className="mt-1 text-xs text-slate-500">
                  Name, NIC and date of birth cannot be changed here.
                </p>

                {profileUpdateStatus === 'failed' && profileServerMessage && (
                  <Banner tone="error" title="Profile Not Updated" role="alert" className="mt-4">
                    {profileServerMessage}
                  </Banner>
                )}

                <div className="mt-4 grid gap-5 sm:grid-cols-2">
                  <div className="sm:col-span-2">
                    <RequiredLegend />
                  </div>

                  <Field
                    id="profile-phone-number"
                    label="Phone Number"
                    required
                    hint="For example 0771234567 or +94771234567."
                    error={profileFieldErrors.phoneNumber}
                  >
                    {(control) => (
                      <input
                        {...control}
                        type="tel"
                        value={profileForm.phoneNumber}
                        onChange={(event) => {
                          setProfileForm((previous) =>
                            previous ? { ...previous, phoneNumber: event.target.value } : previous,
                          );
                          clearProfileFieldError('phoneNumber');
                        }}
                        disabled={isSavingProfile}
                      />
                    )}
                  </Field>

                  <Field id="profile-blood-group" label="Blood Group" required error={profileFieldErrors.bloodGroup}>
                    {(control) => (
                      <select
                        {...control}
                        value={profileForm.bloodGroup}
                        onChange={(event) => {
                          setProfileForm((previous) =>
                            previous ? { ...previous, bloodGroup: event.target.value as BloodGroup } : previous,
                          );
                          clearProfileFieldError('bloodGroup');
                        }}
                        disabled={isSavingProfile}
                      >
                        {BLOOD_GROUP_OPTIONS.map((option) => (
                          <option key={option} value={option}>
                            {option}
                          </option>
                        ))}
                      </select>
                    )}
                  </Field>

                  <Field
                    id="profile-address"
                    label="Address"
                    required
                    error={profileFieldErrors.address}
                    className="sm:col-span-2"
                  >
                    {(control) => (
                      <textarea
                        {...control}
                        rows={3}
                        value={profileForm.address}
                        onChange={(event) => {
                          setProfileForm((previous) =>
                            previous ? { ...previous, address: event.target.value } : previous,
                          );
                          clearProfileFieldError('address');
                        }}
                        disabled={isSavingProfile}
                      />
                    )}
                  </Field>
                </div>

                <div className="mt-5 flex flex-wrap gap-3">
                  <Button type="submit" loading={isSavingProfile}>
                    {isSavingProfile ? 'Saving…' : 'Save Changes'}
                  </Button>
                  <Button variant="secondary" onClick={cancelProfileEdit} disabled={isSavingProfile}>
                    Cancel
                  </Button>
                </div>
              </form>
            )}
          </SectionCard>

          {(allergies.length > 0 ||
            (user?.role === 'Doctor' && (conditions.length > 0 || overdueFollowUp))) && (
            <div className="space-y-3" role="group" aria-label="Medical alerts">
              {allergies.map((allergy) => (
                <AlertBanner key={allergy.allergyId} tone="allergy" label="Allergy Alert">
                  ⚠️ ALLERGY: {allergy.allergyName} — {allergy.severity}
                </AlertBanner>
              ))}

              {user?.role === 'Doctor' && conditions.map((condition) => (
                <AlertBanner
                  key={condition.conditionId}
                  tone="condition"
                  label="Chronic Condition Alert"
                >
                  ⚠️ CONDITION: {condition.conditionName} (since {formatMonthYear(condition.dateDiagnosed)})
                </AlertBanner>
              ))}

              {user?.role === 'Doctor' && overdueFollowUp && (
                <AlertBanner tone="followUp" label="Follow-up Alert">
                  📌 FOLLOW-UP: {overdueFollowUp.instructions} — overdue
                </AlertBanner>
              )}
            </div>
          )}

          {user?.role === 'Doctor' && followUpLoadFailed && (
            <Banner tone="neutral" role="status">
              Unable to load follow-up alerts.
            </Banner>
          )}

          {isReceptionist && (
            <SectionCard eyebrow="Today's Queue" title="Check-In">
              <div aria-live="polite" className="space-y-3">
                {queueStatusLoadState === 'loading' && <LoadingText>Checking today&apos;s queue…</LoadingText>}

                {checkInStatus === 'succeeded' && queueStatus?.isCheckedIn && (
                  <Banner tone="success" title="Checked In">
                    <p className="font-semibold">
                      {patient.fullName} checked in. Queue: {queueStatus.queueNumber}
                    </p>
                  </Banner>
                )}

                {checkInStatus !== 'succeeded' && queueStatusLoadState === 'loaded' && queueStatus?.isCheckedIn && (
                  <Banner tone="warning" title="In Today's Queue">
                    <p className="font-semibold">Already checked in. Queue: {queueStatus.queueNumber}</p>
                  </Banner>
                )}

                {checkInMessage && checkInStatus === 'failed' && (
                  <Banner tone="error" title="Check-In Failed">
                    {checkInMessage}
                  </Banner>
                )}

                {checkInMessage && checkInStatus === 'accepted' && (
                  <Banner tone="warning" title="Check-In Accepted">
                    {checkInMessage}
                  </Banner>
                )}

                {queueStatusLoadState === 'loaded' &&
                  queueStatus &&
                  !queueStatus.isCheckedIn &&
                  checkInStatus !== 'accepted' && (
                    <div>
                      <p className="text-sm text-slate-600">
                        Adds this patient to today&apos;s waiting queue and gives them a queue number.
                      </p>
                      <Button className="mt-3" loading={isCheckingIn} onClick={handleCheckIn}>
                        {checkInStatus === 'submitting'
                          ? 'Checking In…'
                          : checkInStatus === 'awaitingQueue'
                            ? 'Assigning Queue…'
                            : 'Check In'}
                      </Button>
                    </div>
                  )}

                {queueStatusLoadState === 'error' && checkInStatus !== 'accepted' && (
                  <Banner tone="error" title="Queue Status Unavailable">
                    Unable to check today&apos;s queue status.
                  </Banner>
                )}
              </div>
            </SectionCard>
          )}

          <SectionCard title="Allergies" titleId="allergies-heading">
            <div className="space-y-4">
              {allergiesRefreshFailed && (
                <Banner tone="warning" title="List May Be Out of Date" role="status">
                  Your change was saved, but the list could not be refreshed. Reload the page to see it.
                </Banner>
              )}

              {allergiesLoadState === 'error' ? (
                <Banner tone="error" role="alert">
                  Unable to load allergies.
                </Banner>
              ) : allergies.length === 0 ? (
                <EmptyState>
                  <p>No allergies recorded</p>
                </EmptyState>
              ) : (
                <div className={tableWrapperClassName}>
                  <table className={tableClassName}>
                    <thead className={tableHeadClassName}>
                      <tr>
                        {['Allergy', 'Severity', 'Notes', 'Date Recorded', ...(canManage ? ['Actions'] : [])].map(
                          (heading) => (
                            <th key={heading} scope="col" className={tableHeaderCellClassName}>
                              {heading}
                            </th>
                          ),
                        )}
                      </tr>
                    </thead>
                    <tbody className={tableBodyClassName}>
                      {allergies.map((allergy) =>
                        editingAllergyId === allergy.allergyId ? (
                          <tr key={allergy.allergyId}>
                            <td colSpan={allergyColumnCount} className="bg-slate-50 px-4 py-4">
                              <form
                                onSubmit={(event) => handleEditSubmit(event, allergy.allergyId)}
                                noValidate
                                className="grid gap-4 sm:grid-cols-2"
                              >
                                {editServerMessage && (
                                  <Banner tone="error" title="Allergy Not Saved" role="alert" className="sm:col-span-2">
                                    {editServerMessage}
                                  </Banner>
                                )}
                                <Field
                                  id={`edit-name-${allergy.allergyId}`}
                                  label="Allergy Name"
                                  required
                                  error={editFieldErrors.allergyName}
                                >
                                  {(control) => (
                                    <input
                                      {...control}
                                      type="text"
                                      value={editForm.allergyName}
                                      onChange={(event) => {
                                        setEditForm((prev) => ({ ...prev, allergyName: event.target.value }));
                                        setEditFieldErrors((prev) => ({ ...prev, allergyName: null }));
                                      }}
                                      disabled={editStatus === 'submitting'}
                                    />
                                  )}
                                </Field>
                                <Field
                                  id={`edit-severity-${allergy.allergyId}`}
                                  label="Severity"
                                  required
                                  error={editFieldErrors.severity}
                                >
                                  {(control) => (
                                    <select
                                      {...control}
                                      value={editForm.severity}
                                      onChange={(event) => {
                                        setEditForm((prev) => ({ ...prev, severity: event.target.value as AllergySeverity }));
                                        setEditFieldErrors((prev) => ({ ...prev, severity: null }));
                                      }}
                                      disabled={editStatus === 'submitting'}
                                    >
                                      {SEVERITY_OPTIONS.map((option) => (
                                        <option key={option} value={option}>
                                          {option}
                                        </option>
                                      ))}
                                    </select>
                                  )}
                                </Field>
                                <Field
                                  id={`edit-notes-${allergy.allergyId}`}
                                  label="Notes"
                                  optional
                                  className="sm:col-span-2"
                                >
                                  {(control) => (
                                    <textarea
                                      {...control}
                                      rows={2}
                                      value={editForm.notes}
                                      onChange={(event) => setEditForm((prev) => ({ ...prev, notes: event.target.value }))}
                                      disabled={editStatus === 'submitting'}
                                    />
                                  )}
                                </Field>
                                <div className="flex flex-wrap gap-2 sm:col-span-2">
                                  <Button type="submit" size="sm" loading={editStatus === 'submitting'}>
                                    {editStatus === 'submitting' ? 'Saving…' : 'Save'}
                                  </Button>
                                  <Button
                                    variant="secondary"
                                    size="sm"
                                    onClick={cancelEdit}
                                    disabled={editStatus === 'submitting'}
                                  >
                                    Cancel
                                  </Button>
                                </div>
                              </form>
                            </td>
                          </tr>
                        ) : (
                          <Fragment key={allergy.allergyId}>
                            <tr>
                              <td className={tableKeyCellClassName}>{allergy.allergyName}</td>
                              <td className={tableCellClassName}>
                                <StatusBadge tone={SEVERITY_TONES[allergy.severity]}>{allergy.severity}</StatusBadge>
                              </td>
                              <td className={`break-words ${tableCellClassName}`}>{allergy.notes ?? '-'}</td>
                              <td className={`whitespace-nowrap ${tableCellClassName}`}>
                                {formatDate(allergy.recordedAt)}
                              </td>
                              {canManage && (
                                <td className={tableCellClassName}>
                                  {confirmingRemovalId !== allergy.allergyId && (
                                    <div className="flex flex-wrap gap-2">
                                      <Button variant="secondary" size="sm" onClick={() => startEdit(allergy)}>
                                        Edit
                                      </Button>
                                      <Button
                                        variant="secondary"
                                        size="sm"
                                        onClick={() => {
                                          setRemoveServerMessage(null);
                                          setConfirmingRemovalId(allergy.allergyId);
                                        }}
                                      >
                                        Remove
                                      </Button>
                                    </div>
                                  )}
                                </td>
                              )}
                            </tr>
                            {canManage && confirmingRemovalId === allergy.allergyId && (
                              <tr>
                                <td colSpan={allergyColumnCount} className="p-0">
                                  <ConfirmPanel
                                    labelId={`remove-allergy-${allergy.allergyId}`}
                                    title="Remove Allergy"
                                    confirmLabel="Confirm"
                                    busyLabel="Removing…"
                                    busy={isRemovingAllergy}
                                    error={removeServerMessage}
                                    onConfirm={() => handleConfirmRemove(allergy.allergyId)}
                                    onCancel={() => {
                                      setRemoveServerMessage(null);
                                      setConfirmingRemovalId(null);
                                    }}
                                  >
                                    Are you sure you want to remove this allergy? This cannot be undone.
                                  </ConfirmPanel>
                                </td>
                              </tr>
                            )}
                          </Fragment>
                        ),
                      )}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            {canManage && (
              <div className="mt-6 border-t border-slate-200 pt-5">
                <h3 className={SUB_HEADING_CLASS_NAME}>Add Allergy</h3>

                <div aria-live="polite">
                  {addStatus === 'failed' && addServerMessage && (
                    <Banner tone="error" title="Allergy Not Added" className="mt-3">
                      {addServerMessage}
                    </Banner>
                  )}
                </div>

                <form onSubmit={handleAddSubmit} noValidate className="mt-4 grid gap-5 sm:grid-cols-2">
                  <div className="sm:col-span-2">
                    <RequiredLegend />
                  </div>

                  <Field id="add-allergy-name" label="Allergy Name" required error={addFieldErrors.allergyName}>
                    {(control) => (
                      <input
                        {...control}
                        type="text"
                        autoComplete="off"
                        value={addForm.allergyName}
                        onChange={(event) => {
                          setAddForm((prev) => ({ ...prev, allergyName: event.target.value }));
                          setAddFieldErrors((prev) => ({ ...prev, allergyName: null }));
                        }}
                        disabled={addStatus === 'submitting'}
                      />
                    )}
                  </Field>

                  <Field id="add-severity" label="Severity" required error={addFieldErrors.severity}>
                    {(control) => (
                      <select
                        {...control}
                        value={addForm.severity}
                        onChange={(event) => {
                          setAddForm((prev) => ({ ...prev, severity: event.target.value as AllergySeverity }));
                          setAddFieldErrors((prev) => ({ ...prev, severity: null }));
                        }}
                        disabled={addStatus === 'submitting'}
                      >
                        {SEVERITY_OPTIONS.map((option) => (
                          <option key={option} value={option}>
                            {option}
                          </option>
                        ))}
                      </select>
                    )}
                  </Field>

                  <Field id="add-notes" label="Notes" optional className="sm:col-span-2">
                    {(control) => (
                      <textarea
                        {...control}
                        rows={2}
                        value={addForm.notes}
                        onChange={(event) => setAddForm((prev) => ({ ...prev, notes: event.target.value }))}
                        disabled={addStatus === 'submitting'}
                      />
                    )}
                  </Field>

                  <div className="sm:col-span-2">
                    <Button type="submit" loading={addStatus === 'submitting'}>
                      {addStatus === 'submitting' ? 'Saving…' : 'Add Allergy'}
                    </Button>
                  </div>
                </form>
              </div>
            )}
          </SectionCard>

          <SectionCard title="Chronic Conditions" titleId="conditions-heading">
            {conditionsLoadState === 'error' ? (
              <Banner tone="error" role="alert">
                Unable to load chronic conditions.
              </Banner>
            ) : conditions.length === 0 ? (
              <EmptyState>
                <p>No chronic conditions recorded</p>
              </EmptyState>
            ) : (
              <div className={tableWrapperClassName}>
                <table className={tableClassName}>
                  <thead className={tableHeadClassName}>
                    <tr>
                      {['Condition', 'Date Diagnosed', 'Notes', ...(isReceptionist ? ['Actions'] : [])].map(
                        (heading) => (
                          <th key={heading} scope="col" className={tableHeaderCellClassName}>
                            {heading}
                          </th>
                        ),
                      )}
                    </tr>
                  </thead>
                  <tbody className={tableBodyClassName}>
                    {conditions.map((condition) => (
                      <Fragment key={condition.conditionId}>
                        <tr>
                          <td className={tableKeyCellClassName}>{condition.conditionName}</td>
                          <td className={`whitespace-nowrap ${tableCellClassName}`}>
                            {formatDate(condition.dateDiagnosed.slice(0, 10))}
                          </td>
                          <td className={`break-words ${tableCellClassName}`}>{condition.notes ?? '-'}</td>
                          {isReceptionist && (
                            <td className={tableCellClassName}>
                              {confirmingConditionRemovalId !== condition.conditionId && (
                                <Button
                                  variant="secondary"
                                  size="sm"
                                  onClick={() => {
                                    setConditionRemoveServerMessage(null);
                                    setConfirmingConditionRemovalId(condition.conditionId);
                                  }}
                                >
                                  Remove
                                </Button>
                              )}
                            </td>
                          )}
                        </tr>
                        {isReceptionist && confirmingConditionRemovalId === condition.conditionId && (
                          <tr>
                            <td colSpan={conditionColumnCount} className="p-0">
                              <ConfirmPanel
                                labelId={`remove-condition-${condition.conditionId}`}
                                title="Remove Condition"
                                confirmLabel="Confirm"
                                busyLabel="Removing…"
                                busy={isRemovingCondition}
                                error={conditionRemoveServerMessage}
                                onConfirm={() => handleConfirmConditionRemove(condition.conditionId)}
                                onCancel={() => {
                                  setConditionRemoveServerMessage(null);
                                  setConfirmingConditionRemovalId(null);
                                }}
                              >
                                Are you sure you want to remove this condition? This cannot be undone.
                              </ConfirmPanel>
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {isReceptionist && conditionsLoadState !== 'error' && (
              <div className="mt-6 border-t border-slate-200 pt-5">
                <h3 className={SUB_HEADING_CLASS_NAME}>Add Condition</h3>

                <div aria-live="polite">
                  {conditionAddStatus === 'failed' && conditionAddServerMessage && (
                    <Banner tone="error" title="Condition Not Added" className="mt-3">
                      {conditionAddServerMessage}
                    </Banner>
                  )}
                </div>

                <form onSubmit={handleConditionAddSubmit} noValidate className="mt-4 grid gap-5 sm:grid-cols-2">
                  <div className="sm:col-span-2">
                    <RequiredLegend />
                  </div>

                  <Field
                    id="condition-name"
                    label="Condition Name"
                    required
                    error={conditionFieldErrors.conditionName}
                  >
                    {(control) => (
                      <input
                        {...control}
                        type="text"
                        autoComplete="off"
                        maxLength={128}
                        value={conditionForm.conditionName}
                        onChange={(event) => {
                          setConditionForm((previous) => ({ ...previous, conditionName: event.target.value }));
                          setConditionFieldErrors((previous) => ({ ...previous, conditionName: null }));
                        }}
                        disabled={conditionAddStatus === 'submitting'}
                      />
                    )}
                  </Field>

                  <Field
                    id="condition-date-diagnosed"
                    label="Date Diagnosed"
                    required
                    hint="Today or earlier."
                    error={conditionFieldErrors.dateDiagnosed}
                  >
                    {(control) => (
                      <input
                        {...control}
                        type="date"
                        max={clinicTodayForDateInput()}
                        value={conditionForm.dateDiagnosed}
                        onChange={(event) => {
                          setConditionForm((previous) => ({ ...previous, dateDiagnosed: event.target.value }));
                          setConditionFieldErrors((previous) => ({ ...previous, dateDiagnosed: null }));
                        }}
                        disabled={conditionAddStatus === 'submitting'}
                      />
                    )}
                  </Field>

                  <Field id="condition-notes" label="Notes" optional className="sm:col-span-2">
                    {(control) => (
                      <textarea
                        {...control}
                        rows={3}
                        maxLength={512}
                        value={conditionForm.notes}
                        onChange={(event) => setConditionForm((previous) => ({ ...previous, notes: event.target.value }))}
                        disabled={conditionAddStatus === 'submitting'}
                      />
                    )}
                  </Field>

                  <div className="sm:col-span-2">
                    <Button type="submit" loading={conditionAddStatus === 'submitting'}>
                      {conditionAddStatus === 'submitting' ? 'Saving…' : 'Add Condition'}
                    </Button>
                  </div>
                </form>
              </div>
            )}
          </SectionCard>
        </>
      )}
    </DashboardShell>
  );
}
