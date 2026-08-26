import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';
import { registerPatient } from '../api/patients';
import type { BloodGroup, Gender, RegisterPatientRequestBody, RegisteredPatient } from '../api/patients';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';

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

function inputClassName(hasError: boolean): string {
  return `mt-1.5 block w-full border-2 bg-white px-3 py-2.5 text-sm text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:bg-slate-100 disabled:text-slate-400 ${
    hasError ? 'border-red-600' : 'border-slate-400 focus:border-brand-blue'
  }`;
}

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
  return new Date().toISOString().slice(0, 10);
}

export function PatientRegistrationPage() {
  const { user } = useAuth();
  const backRoute = user ? roleRoutes[user.role] : '/login';

  const [nic, setNic] = useState('');
  const [fullName, setFullName] = useState('');
  const [dateOfBirth, setDateOfBirth] = useState('');
  const [gender, setGender] = useState<Gender>('Male');
  const [address, setAddress] = useState('');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [bloodGroup, setBloodGroup] = useState<BloodGroup>('O+');

  const [fieldErrors, setFieldErrors] = useState<FieldErrors>(EMPTY_FIELD_ERRORS);
  const [status, setStatus] = useState<SubmissionStatus>('idle');
  const [serverMessage, setServerMessage] = useState<string | null>(null);
  const [registeredPatient, setRegisteredPatient] = useState<RegisteredPatient | null>(null);

  const isBusy = status === 'submitting';

  function clearFieldError(field: keyof FieldErrors) {
    setFieldErrors((prev) => (prev[field] ? { ...prev, [field]: null } : prev));
    if (status === 'failed') {
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
      gender,
      address: trimmedAddress,
      phoneNumber: trimmedPhoneNumber,
      bloodGroup,
    };

    try {
      const patient = await registerPatient(request);
      setStatus('created');
      setRegisteredPatient(patient);
      setNic('');
      setFullName('');
      setDateOfBirth('');
      setGender('Male');
      setAddress('');
      setPhoneNumber('');
      setBloodGroup('O+');
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
      <Link
        to={backRoute}
        className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
      >
        ← Back to Dashboard
      </Link>

      {/* Status region - one persistent aria-live container, content swapped by status */}
      <div aria-live="polite">
        {status === 'created' && registeredPatient && (
          <div className="mt-6 border-t-4 border-b border-emerald-700 bg-emerald-50 px-6 py-3">
            <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-emerald-800">Patient Registered</p>
            <p className="mt-1 text-sm text-emerald-900">
              Patient registered successfully. Patient ID: {registeredPatient.patientId}
            </p>
          </div>
        )}
        {status === 'failed' && serverMessage && (
          <div className="mt-6 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3">
            <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">Patient Not Registered</p>
            <p className="mt-1 text-sm text-red-900">{serverMessage}</p>
          </div>
        )}
      </div>

      <form onSubmit={handleSubmit} noValidate className="mt-6 space-y-5 border border-slate-300 bg-white px-6 py-6">
        <div>
          <label htmlFor="nic" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            NIC
          </label>
          <input
            id="nic"
            name="nic"
            type="text"
            autoComplete="off"
            value={nic}
            onChange={(event) => {
              setNic(event.target.value);
              clearFieldError('nic');
            }}
            disabled={isBusy}
            aria-invalid={fieldErrors.nic ? true : undefined}
            aria-describedby={fieldErrors.nic ? 'nic-error' : undefined}
            className={inputClassName(!!fieldErrors.nic)}
          />
          {fieldErrors.nic && (
            <p id="nic-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.nic}
            </p>
          )}
        </div>

        <div>
          <label htmlFor="fullName" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            Full Name
          </label>
          <input
            id="fullName"
            name="fullName"
            type="text"
            autoComplete="off"
            value={fullName}
            onChange={(event) => {
              setFullName(event.target.value);
              clearFieldError('fullname');
            }}
            disabled={isBusy}
            aria-invalid={fieldErrors.fullname ? true : undefined}
            aria-describedby={fieldErrors.fullname ? 'fullName-error' : undefined}
            className={inputClassName(!!fieldErrors.fullname)}
          />
          {fieldErrors.fullname && (
            <p id="fullName-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.fullname}
            </p>
          )}
        </div>

        <div>
          <label htmlFor="dateOfBirth" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            Date of Birth
          </label>
          <input
            id="dateOfBirth"
            name="dateOfBirth"
            type="date"
            max={todayAsDateInputValue()}
            value={dateOfBirth}
            onChange={(event) => {
              setDateOfBirth(event.target.value);
              clearFieldError('dateofbirth');
            }}
            disabled={isBusy}
            aria-invalid={fieldErrors.dateofbirth ? true : undefined}
            aria-describedby={fieldErrors.dateofbirth ? 'dateOfBirth-error' : undefined}
            className={inputClassName(!!fieldErrors.dateofbirth)}
          />
          {fieldErrors.dateofbirth && (
            <p id="dateOfBirth-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.dateofbirth}
            </p>
          )}
        </div>

        <div>
          <label htmlFor="gender" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            Gender
          </label>
          <select
            id="gender"
            name="gender"
            value={gender}
            onChange={(event) => {
              setGender(event.target.value as Gender);
              clearFieldError('gender');
            }}
            disabled={isBusy}
            aria-invalid={fieldErrors.gender ? true : undefined}
            aria-describedby={fieldErrors.gender ? 'gender-error' : undefined}
            className={inputClassName(!!fieldErrors.gender)}
          >
            {GENDER_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
          {fieldErrors.gender && (
            <p id="gender-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.gender}
            </p>
          )}
        </div>

        <div>
          <label htmlFor="address" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            Address
          </label>
          <textarea
            id="address"
            name="address"
            rows={2}
            value={address}
            onChange={(event) => {
              setAddress(event.target.value);
              clearFieldError('address');
            }}
            disabled={isBusy}
            aria-invalid={fieldErrors.address ? true : undefined}
            aria-describedby={fieldErrors.address ? 'address-error' : undefined}
            className={inputClassName(!!fieldErrors.address)}
          />
          {fieldErrors.address && (
            <p id="address-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.address}
            </p>
          )}
        </div>

        <div>
          <label htmlFor="phoneNumber" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            Phone Number
          </label>
          <input
            id="phoneNumber"
            name="phoneNumber"
            type="tel"
            autoComplete="off"
            placeholder="0771234567 or +94771234567"
            value={phoneNumber}
            onChange={(event) => {
              setPhoneNumber(event.target.value);
              clearFieldError('phonenumber');
            }}
            disabled={isBusy}
            aria-invalid={fieldErrors.phonenumber ? true : undefined}
            aria-describedby={fieldErrors.phonenumber ? 'phoneNumber-error' : undefined}
            className={inputClassName(!!fieldErrors.phonenumber)}
          />
          {fieldErrors.phonenumber && (
            <p id="phoneNumber-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.phonenumber}
            </p>
          )}
        </div>

        <div>
          <label htmlFor="bloodGroup" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            Blood Group
          </label>
          <select
            id="bloodGroup"
            name="bloodGroup"
            value={bloodGroup}
            onChange={(event) => {
              setBloodGroup(event.target.value as BloodGroup);
              clearFieldError('bloodgroup');
            }}
            disabled={isBusy}
            aria-invalid={fieldErrors.bloodgroup ? true : undefined}
            aria-describedby={fieldErrors.bloodgroup ? 'bloodGroup-error' : undefined}
            className={inputClassName(!!fieldErrors.bloodgroup)}
          >
            {BLOOD_GROUP_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
          {fieldErrors.bloodgroup && (
            <p id="bloodGroup-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.bloodgroup}
            </p>
          )}
        </div>

        <button
          type="submit"
          disabled={isBusy}
          className="relative w-full overflow-hidden bg-brand-blue px-4 py-3 text-sm font-bold uppercase tracking-[0.15em] text-white hover:bg-brand-blue-dark focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
        >
          {isBusy ? 'Registering…' : 'Register Patient'}
          {isBusy && (
            <span className="absolute inset-x-0 bottom-0 block h-0.5 overflow-hidden bg-white/20" aria-hidden="true">
              <span className="block h-full w-1/3 animate-[loading-sweep_1.1s_ease-in-out_infinite] bg-white" />
            </span>
          )}
        </button>
      </form>
    </DashboardShell>
  );
}
