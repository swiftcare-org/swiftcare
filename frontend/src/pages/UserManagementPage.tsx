import { Fragment, useCallback, useEffect, useState, type FormEvent } from 'react';
import { DashboardShell } from '../dashboards/DashboardShell';
import { createUser, deactivateUser, listUsers, reactivateUser } from '../api/users';
import type { CreateUserRequestBody, UserSummary } from '../api/users';
import { ApiError } from '../api/client';
import type { UserRole } from '../auth/types';
import { useAuth } from '../auth/useAuth';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { ConfirmPanel } from '../components/ui/ConfirmPanel';
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
  textLinkClassName,
} from '../components/ui/table';
import { EditUserForm } from '../users/EditUserForm';
import { ResetPasswordForm } from '../users/ResetPasswordForm';
import { MINIMUM_PASSWORD_LENGTH } from '../users/passwordPolicy';

type SubmissionStatus = 'idle' | 'submitting' | 'created' | 'failed';
type ListStatus = 'loading' | 'loaded' | 'error';
type PanelKind = 'edit' | 'reset' | 'deactivate' | 'reactivate';

// The one panel open under a row. Only one is open at a time, so two unsaved forms
// can never be on screen together.
interface RowPanel {
  userId: string;
  kind: PanelKind;
}

interface Notice {
  tone: 'success' | 'error';
  title: string;
  message: string;
}

const COLUMNS = ['Username', 'Full Name', 'Role', 'Room', 'Specialization', 'Status', 'Actions'];

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
  const { user: currentUser } = useAuth();

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [fullName, setFullName] = useState('');
  const [role, setRole] = useState<UserRole | ''>('');
  const [roomNumber, setRoomNumber] = useState('');

  const [fieldErrors, setFieldErrors] = useState<FieldErrors>(EMPTY_FIELD_ERRORS);
  const [status, setStatus] = useState<SubmissionStatus>('idle');
  const [serverMessage, setServerMessage] = useState<string | null>(null);

  const [users, setUsers] = useState<UserSummary[]>([]);
  const [listStatus, setListStatus] = useState<ListStatus>('loading');

  const [panel, setPanel] = useState<RowPanel | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [statusBusy, setStatusBusy] = useState(false);
  const [statusError, setStatusError] = useState<string | null>(null);

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

  function openPanel(target: UserSummary, kind: PanelKind) {
    setNotice(null);
    setStatusError(null);
    if (status === 'created' || status === 'failed') {
      setStatus('idle');
      setServerMessage(null);
    }

    // The server refuses this too; saying so here saves a confirmation that cannot succeed.
    if (kind === 'deactivate' && target.userId === currentUser?.userId) {
      setPanel(null);
      setNotice({
        tone: 'error',
        title: 'Account Not Deactivated',
        message: 'You cannot deactivate your own account.',
      });
      return;
    }

    setPanel({ userId: target.userId, kind });
  }

  function closePanel() {
    setPanel(null);
    setStatusError(null);
  }

  function replaceUser(updated: UserSummary) {
    setUsers((prev) => prev.map((existing) => (existing.userId === updated.userId ? updated : existing)));
  }

  function handleEdited(updated: UserSummary) {
    replaceUser(updated);
    setPanel(null);
    setNotice({
      tone: 'success',
      title: 'Account Updated',
      message: `The details for ${updated.username} were saved.`,
    });
  }

  function handlePasswordReset(target: UserSummary) {
    setPanel(null);
    setNotice({
      tone: 'success',
      title: 'Password Reset',
      message: `${target.username} can sign in with the new password now.`,
    });
  }

  async function handleStatusChange(target: UserSummary, deactivate: boolean) {
    setStatusBusy(true);
    setStatusError(null);

    try {
      replaceUser(deactivate ? await deactivateUser(target.userId) : await reactivateUser(target.userId));
      setPanel(null);
      setNotice(
        deactivate
          ? {
              tone: 'success',
              title: 'Account Deactivated',
              message: `${target.username} can no longer sign in.`,
            }
          : {
              tone: 'success',
              title: 'Account Reactivated',
              message: `${target.username} can sign in again.`,
            },
      );
    } catch (error) {
      if (deactivate && error instanceof ApiError && error.status === 400) {
        setStatusError('You cannot deactivate your own account.');
      } else if (error instanceof ApiError && error.status === 404) {
        setStatusError('This account no longer exists. Please refresh the page.');
      } else {
        setStatusError(`Unable to ${deactivate ? 'deactivate' : 'reactivate'} the account. Please try again.`);
      }
    } finally {
      setStatusBusy(false);
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
    setNotice(null);

    const request: CreateUserRequestBody = {
      username: trimmedUsername,
      password,
      fullName: trimmedFullName,
      role: role as UserRole,
      roomNumber: role === 'Doctor' ? trimmedRoomNumber : undefined,
    };

    try {
      await createUser(request);
      setStatus('created');
      setUsername('');
      setPassword('');
      setFullName('');
      setRole('');
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
    <DashboardShell sectionLabel="Staff Accounts">
      {/* Status region - one persistent aria-live container, content swapped by status */}
      <div aria-live="polite" className="empty:hidden">
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

      <SectionCard title="Create Account" description="Add a doctor, receptionist or administrator.">
        <form onSubmit={handleSubmit} noValidate className="grid max-w-3xl gap-5 sm:grid-cols-2">
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
                <option value="" disabled>
                  Select role
                </option>
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

      <SectionCard title="All Accounts" description={accountCount}>
        {listStatus === 'loading' && <LoadingText>Loading users…</LoadingText>}
        {listStatus === 'error' && (
          <Banner tone="error" title="Staff Accounts Unavailable" role="alert">
            Unable to load the user list. Please refresh the page.
          </Banner>
        )}
        {listStatus === 'loaded' && users.length === 0 && <EmptyState>No accounts yet.</EmptyState>}

        {/* Outcome of a row action, shown beside the table it changed. */}
        <div aria-live="polite" className="empty:hidden" data-testid="user-action-notice">
          {notice && (
            <Banner tone={notice.tone} title={notice.title} className="mb-4">
              {notice.message}
            </Banner>
          )}
        </div>

        {listStatus === 'loaded' && users.length > 0 && (
          <div className={tableWrapperClassName}>
            <table className={tableClassName}>
              <thead className={tableHeadClassName}>
                <tr>
                  {COLUMNS.map((heading) => (
                    <th key={heading} scope="col" className={tableHeaderCellClassName}>
                      {heading}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className={tableBodyClassName}>
                {users.map((listedUser) => {
                  const openKind = panel?.userId === listedUser.userId ? panel.kind : null;
                  const rowAction = (kind: PanelKind, label: string, testId: string) => (
                    <button
                      type="button"
                      className={textLinkClassName}
                      aria-expanded={openKind === kind}
                      data-testid={testId}
                      onClick={() => (openKind === kind ? closePanel() : openPanel(listedUser, kind))}
                    >
                      {label}
                      <span className="sr-only"> for {listedUser.username}</span>
                    </button>
                  );

                  return (
                    <Fragment key={listedUser.userId}>
                      <tr data-testid="user-row">
                        <td className={tableKeyCellClassName}>{listedUser.username}</td>
                        <td className={tableCellClassName}>{listedUser.fullName}</td>
                        <td className={tableCellClassName}>{listedUser.role}</td>
                        <td className={tableCellClassName}>{listedUser.roomNumber ?? '-'}</td>
                        <td className={tableCellClassName}>{listedUser.specialization ?? '-'}</td>
                        <td className={tableCellClassName} data-testid="user-status">
                          <StatusBadge tone={listedUser.isActive ? 'success' : 'neutral'}>
                            {listedUser.isActive ? 'Active' : 'Inactive'}
                          </StatusBadge>
                        </td>
                        <td className={tableCellClassName}>
                          <div className="flex gap-4 whitespace-nowrap">
                            {rowAction('edit', 'Edit', 'user-edit')}
                            {rowAction('reset', 'Reset Password', 'user-reset-password')}
                            {listedUser.isActive
                              ? rowAction('deactivate', 'Deactivate', 'user-deactivate')
                              : rowAction('reactivate', 'Reactivate', 'user-reactivate')}
                          </div>
                        </td>
                      </tr>

                      {openKind && (
                        <tr data-testid="user-panel">
                          <td colSpan={COLUMNS.length} className="bg-slate-50 px-4 py-5">
                            {openKind === 'edit' && (
                              <EditUserForm user={listedUser} onSaved={handleEdited} onCancel={closePanel} />
                            )}
                            {openKind === 'reset' && (
                              <ResetPasswordForm
                                user={listedUser}
                                onReset={() => handlePasswordReset(listedUser)}
                                onCancel={closePanel}
                              />
                            )}
                            {openKind === 'deactivate' && (
                              <ConfirmPanel
                                labelId={`deactivate-${listedUser.userId}`}
                                title={`Deactivate ${listedUser.username}`}
                                confirmLabel="Deactivate"
                                busyLabel="Deactivating…"
                                busy={statusBusy}
                                error={statusError}
                                onConfirm={() => handleStatusChange(listedUser, true)}
                                onCancel={closePanel}
                                className="max-w-xl"
                              >
                                Are you sure you want to deactivate this account?
                              </ConfirmPanel>
                            )}
                            {openKind === 'reactivate' && (
                              <ConfirmPanel
                                labelId={`reactivate-${listedUser.userId}`}
                                title={`Reactivate ${listedUser.username}`}
                                confirmLabel="Reactivate"
                                busyLabel="Reactivating…"
                                tone="primary"
                                busy={statusBusy}
                                error={statusError}
                                onConfirm={() => handleStatusChange(listedUser, false)}
                                onCancel={closePanel}
                                className="max-w-xl"
                              >
                                Are you sure you want to reactivate this account?
                              </ConfirmPanel>
                            )}
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>
    </DashboardShell>
  );
}
