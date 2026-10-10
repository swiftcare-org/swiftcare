import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';
import { registerPatient } from '../api/patients';
import type { BloodGroup, Gender, RegisterPatientRequestBody, RegisteredPatient } from '../api/patients';
import { ApiError } from '../api/client';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { Field, RequiredLegend } from '../components/ui/Field';
import { SectionCard } from '../components/ui/SectionCard';
import { textLinkClassName } from '../components/ui/table';
import { clinicTodayForDateInput, formatDate } from '../lib/format';

type SubmissionStatus = 'idle' | 'submitting' | 'created' | 'failed';

interface FieldErrors {
  nic: string | null;
  fullname: string | null;
  dateofbirth: string | null;
  gender: string | null;
  address: string | null;
  phonenumber: string | null;
  bloodgroup: string | null;
}

const EMPTY_FIELD_ERRORS: FieldErrors = {
  nic: null,
  fullname: null,
  dateofbirth: null,
  gender: null,
  address: null,
  phonenumber: null,
  bloodgroup: null,
};

// Mirrors PatientService's RegisterPatientRequest validation attributes. The frontend has
// no way to import those C# rules, so they're kept in sync by hand - the server remains
// authoritative regardless of what this pre-submit check catches.
const NIC_PATTERN = /^([0-9]{9}[VvXx]|[0-9]{12})$/;
const PHONE_PATTERN = /^(0[0-9]{9}|\+94[0-9]{9})$/;
const MINIMUM_BIRTH_YEAR_OFFSET = 130;

const GENDER_OPTIONS: Gender[] = ['Male', 'Female', 'Other'];
const BLOOD_GROUP_OPTIONS: BloodGroup[] = ['A+', 'A-', 'B+', 'B-', 'O+', 'O-', 'AB+', 'AB-'];

const GENERIC_ERROR_MESSAGE = 'Unable to register the patient. Please try again.';

// Server errors are keyed by lowercased field name (see ApiError.fieldErrors); an unknown
// key is silently ignored rather than merged, since this form has a fixed field set.
function applyServerFieldErrors(prev: FieldErrors, serverErrors: Readonly<Record<string, string>>): FieldErrors {
  return {
    nic: serverErrors.nic ?? prev.nic,
    fullname: serverErrors.fullname ?? prev.fullname,
    dateofbirth: serverErrors.dateofbirth ?? prev.dateofbirth,
    gender: serverErrors.gender ?? prev.gender,
    address: serverErrors.address ?? prev.address,
    phonenumber: serverErrors.phonenumber ?? prev.phonenumber,
    bloodgroup: serverErrors.bloodgroup ?? prev.bloodgroup,
  };
}

function todayAsDateInputValue(): string {
  return clinicTodayForDateInput();
}

export function PatientRegistrationPage() {

  const [nic, setNic] = useState('');
  const [fullName, setFullName] = useState('');
  const [dateOfBirth, setDateOfBirth] = useState('');
  // Nothing is pre-selected: a default would be saved as fact if the receptionist skipped it.
  const [gender, setGender] = useState<Gender | ''>('');
  const [address, setAddress] = useState('');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [bloodGroup, setBloodGroup] = useState<BloodGroup | ''>('');

  const [fieldErrors, setFieldErrors] = useState<FieldErrors>(EMPTY_FIELD_ERRORS);
  const [status, setStatus] = useState<SubmissionStatus>('idle');
  const [serverMessage, setServerMessage] = useState<string | null>(null);
  const [registeredPatient, setRegisteredPatient] = useState<RegisteredPatient | null>(null);

  const isBusy = status === 'submitting';

  function clearFieldError(field: keyof FieldErrors) {
    setFieldErrors((prev) => (prev[field] ? { ...prev, [field]: null } : prev));
    if (status === 'failed' || status === 'created') {
      setStatus('idle');
      setServerMessage(null);
    }
  }

  function validateDateOfBirth(value: string): string | null {
    if (!value) {
      return 'Date of birth is required.';
    }
    const parsed = new Date(`${value}T00:00:00`);
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    if (parsed > today) {
      return 'Date of birth must be a valid past date.';
    }
    const earliestAllowed = new Date(today);
    earliestAllowed.setFullYear(earliestAllowed.getFullYear() - MINIMUM_BIRTH_YEAR_OFFSET);
    if (parsed < earliestAllowed) {
      return 'Date of birth must be a valid past date.';
    }
    return null;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    const trimmedNic = nic.trim().toUpperCase();
    const trimmedFullName = fullName.trim();
    const trimmedAddress = address.trim();
    const trimmedPhoneNumber = phoneNumber.trim();

    const nextFieldErrors: FieldErrors = {
      nic: trimmedNic
        ? NIC_PATTERN.test(trimmedNic)
          ? null
          : 'NIC must be 9 digits followed by V/X, or 12 digits.'
        : 'NIC is required.',
      fullname: trimmedFullName ? null : 'Full name is required.',
      dateofbirth: validateDateOfBirth(dateOfBirth),
      gender: gender ? null : 'Gender is required.',
      address: trimmedAddress ? null : 'Address is required.',
      phonenumber: trimmedPhoneNumber
        ? PHONE_PATTERN.test(trimmedPhoneNumber)
          ? null
          : 'Phone number must be a valid Sri Lankan number.'
        : 'Phone number is required.',
      bloodgroup: bloodGroup ? null : 'Blood group is required.',
    };
    setFieldErrors(nextFieldErrors);

    if (Object.values(nextFieldErrors).some((error) => error !== null)) {
      // Client-side validation failure - no network request is made.
      return;
    }

    setStatus('submitting');
    setServerMessage(null);

    const request: RegisterPatientRequestBody = {
      nic: trimmedNic,
      fullName: trimmedFullName,
      dateOfBirth,
      gender: gender as Gender,
      address: trimmedAddress,
      phoneNumber: trimmedPhoneNumber,
      bloodGroup: bloodGroup as BloodGroup,
    };

    try {
      const patient = await registerPatient(request);
      setStatus('created');
      setRegisteredPatient(patient);
      setNic('');
      setFullName('');
      setDateOfBirth('');
      setGender('');
      setAddress('');
      setPhoneNumber('');
      setBloodGroup('');
      setFieldErrors(EMPTY_FIELD_ERRORS);
    } catch (error) {
      setStatus('failed');
      setRegisteredPatient(null);
      if (error instanceof ApiError && error.status === 400 && Object.keys(error.fieldErrors).length > 0) {
        setFieldErrors((prev) => applyServerFieldErrors(prev, error.fieldErrors));
      } else if (error instanceof ApiError && error.status === 403) {
        setServerMessage('You are not authorized to register patients.');
      } else {
        setServerMessage(GENERIC_ERROR_MESSAGE);
      }
    }
  }

  return (
    <DashboardShell sectionLabel="Register Patient">
      {/* Status region - one persistent aria-live container, content swapped by status */}
      <div aria-live="polite" className="empty:hidden">
        {status === 'created' && registeredPatient && (
          <Banner tone="success" title="Patient Registered">
            <p>Patient registered successfully. Patient ID: {registeredPatient.patientId}</p>
            <p className="mt-1">
              <Link to={`/patients/${registeredPatient.patientId}`} className={textLinkClassName}>
                Open patient profile
              </Link>
            </p>
          </Banner>
        )}
        {status === 'failed' && serverMessage && (
          <Banner tone="error" title="Patient Not Registered">
            {serverMessage}
          </Banner>
        )}
      </div>

      <SectionCard title="Patient Details">
        <form onSubmit={handleSubmit} noValidate className="grid max-w-3xl gap-5 sm:grid-cols-2">
          <div className="sm:col-span-2">
            <RequiredLegend />
          </div>

          <Field id="nic" label="NIC" required hint="9 digits followed by V or X, or 12 digits." error={fieldErrors.nic}>
            {(control) => (
              <input
                {...control}
                name="nic"
                type="text"
                autoComplete="off"
                value={nic}
                onChange={(event) => {
                  setNic(event.target.value);
                  clearFieldError('nic');
                }}
                disabled={isBusy}
              />
            )}
          </Field>

          <Field id="fullName" label="Full Name" required error={fieldErrors.fullname}>
            {(control) => (
              <input
                {...control}
                name="fullName"
                type="text"
                autoComplete="off"
                value={fullName}
                onChange={(event) => {
                  setFullName(event.target.value);
                  clearFieldError('fullname');
                }}
                disabled={isBusy}
              />
            )}
          </Field>

          <Field
            id="dateOfBirth"
            label="Date of Birth"
            required
            hint={dateOfBirth ? `Selected: ${formatDate(dateOfBirth)}` : undefined}
            error={fieldErrors.dateofbirth}
          >
            {(control) => (
              <input
                {...control}
                name="dateOfBirth"
                type="date"
                max={todayAsDateInputValue()}
                value={dateOfBirth}
                onChange={(event) => {
                  setDateOfBirth(event.target.value);
                  clearFieldError('dateofbirth');
                }}
                disabled={isBusy}
              />
            )}
          </Field>

          <Field id="gender" label="Gender" required error={fieldErrors.gender}>
            {(control) => (
              <select
                {...control}
                name="gender"
                value={gender}
                onChange={(event) => {
                  setGender(event.target.value as Gender);
                  clearFieldError('gender');
                }}
                disabled={isBusy}
              >
                <option value="" disabled>
                  Select gender
                </option>
                {GENDER_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            )}
          </Field>

          <Field id="phoneNumber" label="Phone Number" required hint="For example 0771234567 or +94771234567." error={fieldErrors.phonenumber}>
            {(control) => (
              <input
                {...control}
                name="phoneNumber"
                type="tel"
                autoComplete="off"
                value={phoneNumber}
                onChange={(event) => {
                  setPhoneNumber(event.target.value);
                  clearFieldError('phonenumber');
                }}
                disabled={isBusy}
              />
            )}
          </Field>

          <Field id="bloodGroup" label="Blood Group" required error={fieldErrors.bloodgroup}>
            {(control) => (
              <select
                {...control}
                name="bloodGroup"
                value={bloodGroup}
                onChange={(event) => {
                  setBloodGroup(event.target.value as BloodGroup);
                  clearFieldError('bloodgroup');
                }}
                disabled={isBusy}
              >
                <option value="" disabled>
                  Select blood group
                </option>
                {BLOOD_GROUP_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            )}
          </Field>

          <Field id="address" label="Address" required error={fieldErrors.address} className="sm:col-span-2">
            {(control) => (
              <textarea
                {...control}
                name="address"
                rows={2}
                value={address}
                onChange={(event) => {
                  setAddress(event.target.value);
                  clearFieldError('address');
                }}
                disabled={isBusy}
              />
            )}
          </Field>

          <div className="sm:col-span-2">
            <Button type="submit" fullWidth loading={isBusy}>
              {isBusy ? 'Registering…' : 'Register Patient'}
            </Button>
          </div>
        </form>
      </SectionCard>
    </DashboardShell>
  );
}
