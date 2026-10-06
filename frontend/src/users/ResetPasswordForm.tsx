import { useState, type FormEvent } from 'react';
import { resetUserPassword, type UserSummary } from '../api/users';
import { ApiError } from '../api/client';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { Field, RequiredLegend } from '../components/ui/Field';
import { MINIMUM_PASSWORD_LENGTH, PASSWORD_TOO_SHORT_MESSAGE } from './passwordPolicy';

interface ResetPasswordFormProps {
  user: UserSummary;
  onReset: () => void;
  onCancel: () => void;
}

export function ResetPasswordForm({ user, onReset, onCancel }: ResetPasswordFormProps) {
  const [newPassword, setNewPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [confirmationError, setConfirmationError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    const nextPasswordError = !newPassword
      ? 'Password is required.'
      : newPassword.length < MINIMUM_PASSWORD_LENGTH
        ? PASSWORD_TOO_SHORT_MESSAGE
        : null;
    const nextConfirmationError = !confirmation
      ? 'Confirm the new password.'
      : confirmation !== newPassword
        ? 'Passwords do not match.'
        : null;
    setPasswordError(nextPasswordError);
    setConfirmationError(nextConfirmationError);

    if (nextPasswordError || nextConfirmationError) {
      return;
    }

    setSaving(true);
    setFormError(null);

    try {
      await resetUserPassword(user.userId, newPassword);
      onReset();
    } catch (error) {
      setSaving(false);
      if (error instanceof ApiError && error.status === 400 && error.fieldErrors.newpassword) {
        setPasswordError(error.fieldErrors.newpassword);
      } else if (error instanceof ApiError && error.status === 404) {
        setFormError('This account no longer exists. Please refresh the page.');
      } else {
        setFormError('Unable to reset the password. Please try again.');
      }
    }
  }

  return (
    <form
      onSubmit={handleSubmit}
      noValidate
      aria-label={`Reset password for ${user.username}`}
      data-testid="reset-password-form"
      className="grid max-w-3xl gap-5 sm:grid-cols-2"
    >
      <div className="sm:col-span-2">
        <RequiredLegend />
      </div>

      {formError && (
        <Banner tone="error" role="alert" className="sm:col-span-2">
          {formError}
        </Banner>
      )}

      <Field
        id="reset-newPassword"
        label="New Password"
        required
        hint={`At least ${MINIMUM_PASSWORD_LENGTH} characters.`}
        error={passwordError}
      >
        {(control) => (
          <input
            {...control}
            name="newPassword"
            type="password"
            autoComplete="new-password"
            value={newPassword}
            onChange={(event) => {
              setNewPassword(event.target.value);
              setPasswordError(null);
              setFormError(null);
            }}
            disabled={saving}
          />
        )}
      </Field>

      <Field id="reset-confirmPassword" label="Confirm New Password" required error={confirmationError}>
        {(control) => (
          <input
            {...control}
            name="confirmPassword"
            type="password"
            autoComplete="new-password"
            value={confirmation}
            onChange={(event) => {
              setConfirmation(event.target.value);
              setConfirmationError(null);
              setFormError(null);
            }}
            disabled={saving}
          />
        )}
      </Field>

      <div className="flex flex-wrap gap-2 sm:col-span-2">
        <Button type="submit" size="sm" loading={saving}>
          {saving ? 'Resetting…' : 'Reset Password'}
        </Button>
        <Button variant="secondary" size="sm" disabled={saving} onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  );
}
