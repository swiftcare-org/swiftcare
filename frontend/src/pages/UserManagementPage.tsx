import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { DashboardShell } from '../dashboards/DashboardShell';
import { createUser, listUsers } from '../api/users';
import type { CreateUserRequestBody, UserSummary } from '../api/users';
import { ApiError } from '../api/client';
import type { UserRole } from '../auth/types';

type SubmissionStatus = 'idle' | 'submitting' | 'created' | 'failed';
type ListStatus = 'loading' | 'loaded' | 'error';

interface FieldErrors {
  username: string | null;
  password: string | null;
  fullname: string | null;
  role: string | null;
  roomnumber: string | null;
}

const EMPTY_FIELD_ERRORS: FieldErrors = {
  username: null,
  password: null,
  fullname: null,
  role: null,
  roomnumber: null,
};

// Mirrors AuthService's PasswordPolicy.MinimumLength. The frontend has no way to import a
// C# constant, so this must be kept in sync by hand - the server remains authoritative
// regardless of what this pre-submit check catches.
const MINIMUM_PASSWORD_LENGTH = 8;

const ROLE_OPTIONS: UserRole[] = ['Doctor', 'Receptionist', 'Admin'];

const GENERIC_ERROR_MESSAGE = 'Unable to create the account. Please try again.';

function inputClassName(hasError: boolean): string {
  return `mt-1.5 block w-full border-2 bg-white px-3 py-2.5 text-sm text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:bg-slate-100 disabled:text-slate-400 ${
    hasError ? 'border-red-600' : 'border-slate-400 focus:border-brand-blue'
  }`;
}

// Server errors are keyed by lowercased field name (see ApiError.fieldErrors); an unknown
// key is silently ignored rather than merged, since this form has a fixed field set.
function applyServerFieldErrors(prev: FieldErrors, serverErrors: Readonly<Record<string, string>>): FieldErrors {
  return {
    username: serverErrors.username ?? prev.username,
    password: serverErrors.password ?? prev.password,
    fullname: serverErrors.fullname ?? prev.fullname,
    role: serverErrors.role ?? prev.role,
    roomnumber: serverErrors.roomnumber ?? prev.roomnumber,
  };
}

export function UserManagementPage() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [fullName, setFullName] = useState('');
  const [role, setRole] = useState<UserRole>('Doctor');
  const [roomNumber, setRoomNumber] = useState('');

  const [fieldErrors, setFieldErrors] = useState<FieldErrors>(EMPTY_FIELD_ERRORS);
  const [status, setStatus] = useState<SubmissionStatus>('idle');
  const [serverMessage, setServerMessage] = useState<string | null>(null);

  const [users, setUsers] = useState<UserSummary[]>([]);
  const [listStatus, setListStatus] = useState<ListStatus>('loading');

  const isBusy = status === 'submitting';

  const loadUsers = useCallback(async () => {
    setListStatus('loading');
    try {
      const result = await listUsers();
      setUsers(result);
      setListStatus('loaded');
    } catch {
      setListStatus('error');
    }
  }, []);

  useEffect(() => {
    loadUsers();
  }, [loadUsers]);

  function clearFieldError(field: keyof FieldErrors) {
    setFieldErrors((prev) => (prev[field] ? { ...prev, [field]: null } : prev));
    if (status === 'failed') {
      setStatus('idle');
      setServerMessage(null);
    }
  }

  function handleRoleChange(value: UserRole) {
    setRole(value);
    clearFieldError('role');
    if (value !== 'Doctor') {
      setRoomNumber('');
      clearFieldError('roomnumber');
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    const trimmedUsername = username.trim();
    const trimmedFullName = fullName.trim();
    const trimmedRoomNumber = roomNumber.trim();

    const nextFieldErrors: FieldErrors = {
      username: trimmedUsername ? null : 'Username is required.',
      password: !password
        ? 'Password is required.'
        : password.length < MINIMUM_PASSWORD_LENGTH
          ? `Password must be at least ${MINIMUM_PASSWORD_LENGTH} characters`
          : null,
      fullname: trimmedFullName ? null : 'Full name is required.',
      role: role ? null : 'Role is required.',
      roomnumber: role === 'Doctor' && !trimmedRoomNumber ? 'Room number is required for doctors' : null,
    };
    setFieldErrors(nextFieldErrors);

    if (Object.values(nextFieldErrors).some((error) => error !== null)) {
      // Client-side validation failure - no network request is made.
      return;
    }

    setStatus('submitting');
    setServerMessage(null);

    const request: CreateUserRequestBody = {
      username: trimmedUsername,
      password,
      fullName: trimmedFullName,
      role,
      roomNumber: role === 'Doctor' ? trimmedRoomNumber : undefined,
    };

    try {
      await createUser(request);
      setStatus('created');
      setUsername('');
      setPassword('');
      setFullName('');
      setRole('Doctor');
      setRoomNumber('');
      setFieldErrors(EMPTY_FIELD_ERRORS);
      await loadUsers();
    } catch (error) {
      setStatus('failed');
      if (error instanceof ApiError && error.status === 400 && Object.keys(error.fieldErrors).length > 0) {
        setFieldErrors((prev) => applyServerFieldErrors(prev, error.fieldErrors));
      } else if (error instanceof ApiError && error.status === 403) {
        setServerMessage('You are not authorized to create users.');
      } else {
        setServerMessage(GENERIC_ERROR_MESSAGE);
      }
    }
  }

  return (
    <DashboardShell sectionLabel="User Management">
      <Link
        to="/admin"
        className="mt-4 inline-block text-xs font-bold uppercase tracking-[0.12em] text-brand-blue hover:text-brand-blue-dark"
      >
        ← Back to Dashboard
      </Link>

      {/* Status region - one persistent aria-live container, content swapped by status */}
      <div aria-live="polite">
        {status === 'created' && (
          <div className="mt-6 border-t-4 border-b border-emerald-700 bg-emerald-50 px-6 py-3">
            <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-emerald-800">Account Created</p>
            <p className="mt-1 text-sm text-emerald-900">The new account was created successfully.</p>
          </div>
        )}
        {status === 'failed' && serverMessage && (
          <div className="mt-6 border-t-4 border-b border-red-700 bg-red-50 px-6 py-3">
            <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">Account Not Created</p>
            <p className="mt-1 text-sm text-red-900">{serverMessage}</p>
          </div>
        )}
      </div>

      <form onSubmit={handleSubmit} noValidate className="mt-6 space-y-5 border border-slate-300 bg-white px-6 py-6">
        <div>
          <label htmlFor="username" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            Username
          </label>
          <input
            id="username"
            name="username"
            type="text"
            autoComplete="off"
            value={username}
            onChange={(event) => {
              setUsername(event.target.value);
              clearFieldError('username');
            }}
            disabled={isBusy}
            aria-invalid={fieldErrors.username ? true : undefined}
            aria-describedby={fieldErrors.username ? 'username-error' : undefined}
            className={inputClassName(!!fieldErrors.username)}
          />
          {fieldErrors.username && (
            <p id="username-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.username}
            </p>
          )}
        </div>

        <div>
          <label htmlFor="password" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            Password
          </label>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(event) => {
              setPassword(event.target.value);
              clearFieldError('password');
            }}
            disabled={isBusy}
            aria-invalid={fieldErrors.password ? true : undefined}
            aria-describedby={fieldErrors.password ? 'password-error' : undefined}
            className={inputClassName(!!fieldErrors.password)}
          />
          {fieldErrors.password && (
            <p id="password-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.password}
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
          <label htmlFor="role" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
            Role
          </label>
          <select
            id="role"
            name="role"
            value={role}
            onChange={(event) => handleRoleChange(event.target.value as UserRole)}
            disabled={isBusy}
            aria-invalid={fieldErrors.role ? true : undefined}
            aria-describedby={fieldErrors.role ? 'role-error' : undefined}
            className={inputClassName(!!fieldErrors.role)}
          >
            {ROLE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
          {fieldErrors.role && (
            <p id="role-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
              {fieldErrors.role}
            </p>
          )}
        </div>

        {role === 'Doctor' && (
          <div>
            <label htmlFor="roomNumber" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
              Room Number
            </label>
            <input
              id="roomNumber"
              name="roomNumber"
              type="text"
              autoComplete="off"
              value={roomNumber}
              onChange={(event) => {
                setRoomNumber(event.target.value);
                clearFieldError('roomnumber');
              }}
              disabled={isBusy}
              aria-invalid={fieldErrors.roomnumber ? true : undefined}
              aria-describedby={fieldErrors.roomnumber ? 'roomNumber-error' : undefined}
              className={inputClassName(!!fieldErrors.roomnumber)}
            />
            {fieldErrors.roomnumber && (
              <p id="roomNumber-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                {fieldErrors.roomnumber}
              </p>
            )}
          </div>
        )}

        <button
          type="submit"
          disabled={isBusy}
          className="relative w-full overflow-hidden bg-brand-blue px-4 py-3 text-sm font-bold uppercase tracking-[0.15em] text-white hover:bg-brand-blue-dark focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2"
        >
          {isBusy ? 'Creating…' : 'Create Account'}
          {isBusy && (
            <span className="absolute inset-x-0 bottom-0 block h-0.5 overflow-hidden bg-white/20" aria-hidden="true">
              <span className="block h-full w-1/3 animate-[loading-sweep_1.1s_ease-in-out_infinite] bg-white" />
            </span>
          )}
        </button>
      </form>

      <div className="mt-8">
        <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-500">Staff Accounts</p>

        {listStatus === 'loading' && <p className="mt-3 text-sm text-slate-500">Loading users…</p>}
        {listStatus === 'error' && <p className="mt-3 text-sm text-red-700">Unable to load the user list.</p>}
        {listStatus === 'loaded' && users.length === 0 && (
          <p className="mt-3 text-sm text-slate-500">No accounts yet.</p>
        )}

        {listStatus === 'loaded' && users.length > 0 && (
          <div className="mt-3 overflow-x-auto border border-slate-300">
            <table className="min-w-full divide-y divide-slate-200 text-sm">
              <thead className="bg-slate-50">
                <tr>
                  <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                    Username
                  </th>
                  <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                    Full Name
                  </th>
                  <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                    Role
                  </th>
                  <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                    Room
                  </th>
                  <th scope="col" className="px-4 py-2 text-left text-xs font-bold uppercase tracking-widest text-slate-600">
                    Status
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200">
                {users.map((listedUser) => (
                  <tr key={listedUser.userId}>
                    <td className="px-4 py-2 text-slate-900">{listedUser.username}</td>
                    <td className="px-4 py-2 text-slate-900">{listedUser.fullName}</td>
                    <td className="px-4 py-2 text-slate-700">{listedUser.role}</td>
                    <td className="px-4 py-2 text-slate-700">{listedUser.roomNumber ?? '—'}</td>
                    <td className="px-4 py-2 text-slate-700">{listedUser.isActive ? 'Active' : 'Inactive'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </DashboardShell>
  );
}
