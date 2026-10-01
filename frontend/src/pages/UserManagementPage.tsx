import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { DashboardShell } from '../dashboards/DashboardShell';
import { createUser, listUsers } from '../api/users';
import type { CreateUserRequestBody, UserSummary } from '../api/users';
import { ApiError } from '../api/client';
import type { UserRole } from '../auth/types';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { EmptyState, LoadingText } from '../components/ui/Feedback';
import { Field, RequiredLegend } from '../components/ui/Field';
import { SectionCard } from '../components/ui/SectionCard';
import { StatusBadge } from '../components/ui/StatusBadge';
import {
  tableBodyClassName,
  tableCellClassName,
  tableClassName,
  tableHeadClassName,
  tableHeaderCellClassName,
  tableKeyCellClassName,
  tableWrapperClassName,
} from '../components/ui/table';

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
    if (status === 'failed' || status === 'created') {
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
          ? `Password must be at least ${MINIMUM_PASSWORD_LENGTH} characters.`
          : null,
      fullname: trimmedFullName ? null : 'Full name is required.',
      role: role ? null : 'Role is required.',
      roomnumber: role === 'Doctor' && !trimmedRoomNumber ? 'Room number is required for doctors.' : null,
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

  const accountCount =
    listStatus === 'loaded' && users.length > 0
      ? `${users.length} ${users.length === 1 ? 'account' : 'accounts'}.`
      : undefined;

  return (
    <DashboardShell sectionLabel="User Management" backLink={{ to: '/admin', destination: 'Dashboard' }}>
      {/* Status region - one persistent aria-live container, content swapped by status */}
      <div aria-live="polite">
        {status === 'created' && (
          <Banner tone="success" title="Account Created">
            The new account was created successfully.
          </Banner>
        )}
        {status === 'failed' && serverMessage && (
          <Banner tone="error" title="Account Not Created">
            {serverMessage}
          </Banner>
        )}
      </div>

      <SectionCard eyebrow="New Account" title="Create Staff Account">
        <form onSubmit={handleSubmit} noValidate className="grid gap-5 sm:grid-cols-2">
          <div className="sm:col-span-2">
            <RequiredLegend />
          </div>

          <Field id="username" label="Username" required error={fieldErrors.username}>
            {(control) => (
              <input
                {...control}
                name="username"
                type="text"
                autoComplete="off"
                value={username}
                onChange={(event) => {
                  setUsername(event.target.value);
                  clearFieldError('username');
                }}
                disabled={isBusy}
              />
            )}
          </Field>

          <Field
            id="password"
            label="Password"
            required
            hint={`At least ${MINIMUM_PASSWORD_LENGTH} characters.`}
            error={fieldErrors.password}
          >
            {(control) => (
              <input
                {...control}
                name="password"
                type="password"
                autoComplete="new-password"
                value={password}
                onChange={(event) => {
                  setPassword(event.target.value);
                  clearFieldError('password');
                }}
                disabled={isBusy}
              />
            )}
          </Field>

          <Field id="fullName" label="Full Name" required error={fieldErrors.fullname} className="sm:col-span-2">
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

          <Field id="role" label="Role" required error={fieldErrors.role}>
            {(control) => (
              <select
                {...control}
                name="role"
                value={role}
                onChange={(event) => handleRoleChange(event.target.value as UserRole)}
                disabled={isBusy}
              >
                {ROLE_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            )}
          </Field>

          {role === 'Doctor' && (
            <Field
              id="roomNumber"
              label="Room Number"
              required
              hint="Patients are called to this room."
              error={fieldErrors.roomnumber}
            >
              {(control) => (
                <input
                  {...control}
                  name="roomNumber"
                  type="text"
                  autoComplete="off"
                  value={roomNumber}
                  onChange={(event) => {
                    setRoomNumber(event.target.value);
                    clearFieldError('roomnumber');
                  }}
                  disabled={isBusy}
                />
              )}
            </Field>
          )}

          <div className="sm:col-span-2">
            <Button type="submit" fullWidth loading={isBusy}>
              {isBusy ? 'Creating…' : 'Create Account'}
            </Button>
          </div>
        </form>
      </SectionCard>

      <SectionCard eyebrow="Directory" title="Staff Accounts" description={accountCount}>
        {listStatus === 'loading' && <LoadingText>Loading users…</LoadingText>}
        {listStatus === 'error' && (
          <Banner tone="error" title="Staff Accounts Unavailable" role="alert">
            Unable to load the user list. Please refresh the page.
          </Banner>
        )}
        {listStatus === 'loaded' && users.length === 0 && <EmptyState>No accounts yet.</EmptyState>}

        {listStatus === 'loaded' && users.length > 0 && (
          <div className={tableWrapperClassName}>
            <table className={tableClassName}>
              <thead className={tableHeadClassName}>
                <tr>
                  {['Username', 'Full Name', 'Role', 'Room', 'Status'].map((heading) => (
                    <th key={heading} scope="col" className={tableHeaderCellClassName}>
                      {heading}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className={tableBodyClassName}>
                {users.map((listedUser) => (
                  <tr key={listedUser.userId}>
                    <td className={tableKeyCellClassName}>{listedUser.username}</td>
                    <td className={tableCellClassName}>{listedUser.fullName}</td>
                    <td className={tableCellClassName}>{listedUser.role}</td>
                    <td className={tableCellClassName}>{listedUser.roomNumber ?? '-'}</td>
                    <td className={tableCellClassName}>
                      <StatusBadge tone={listedUser.isActive ? 'success' : 'neutral'}>
                        {listedUser.isActive ? 'Active' : 'Inactive'}
                      </StatusBadge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>
    </DashboardShell>
  );
}
