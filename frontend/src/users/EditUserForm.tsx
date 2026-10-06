import { useState, type FormEvent } from 'react';
import { updateUser, type UserSummary } from '../api/users';
import { ApiError } from '../api/client';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { Field, RequiredLegend } from '../components/ui/Field';

interface EditUserFormProps {
  user: UserSummary;
  onSaved: (updated: UserSummary) => void;
  onCancel: () => void;
}

interface FieldErrors {
  fullname: string | null;
  roomnumber: string | null;
  specialization: string | null;
}

const NO_ERRORS: FieldErrors = { fullname: null, roomnumber: null, specialization: null };

export function EditUserForm({ user, onSaved, onCancel }: EditUserFormProps) {
  const isDoctor = user.role === 'Doctor';

  const [fullName, setFullName] = useState(user.fullName);
  const [roomNumber, setRoomNumber] = useState(user.roomNumber ?? '');
  const [specialization, setSpecialization] = useState(user.specialization ?? '');
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>(NO_ERRORS);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  function clearError(field: keyof FieldErrors) {
    setFieldErrors((prev) => (prev[field] ? { ...prev, [field]: null } : prev));
    setFormError(null);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    const trimmedFullName = fullName.trim();
    const trimmedRoomNumber = roomNumber.trim();

    const nextErrors: FieldErrors = {
      fullname: trimmedFullName ? null : 'Full name is required.',
      roomnumber: isDoctor && !trimmedRoomNumber ? 'Room number is required for doctors.' : null,
      specialization: null,
    };
    setFieldErrors(nextErrors);

    if (Object.values(nextErrors).some((error) => error !== null)) {
      return;
    }

    setSaving(true);
    setFormError(null);

    try {
      const updated = await updateUser(user.userId, {
        fullName: trimmedFullName,
        roomNumber: isDoctor ? trimmedRoomNumber : undefined,
        specialization: isDoctor ? specialization.trim() || undefined : undefined,
      });
      onSaved(updated);
    } catch (error) {
      setSaving(false);
      if (error instanceof ApiError && error.status === 400 && Object.keys(error.fieldErrors).length > 0) {
        setFieldErrors({
          fullname: error.fieldErrors.fullname ?? null,
          roomnumber: error.fieldErrors.roomnumber ?? null,
          specialization: error.fieldErrors.specialization ?? null,
        });
      } else if (error instanceof ApiError && error.status === 404) {
        setFormError('This account no longer exists. Please refresh the page.');
      } else {
        setFormError('Unable to save the changes. Please try again.');
      }
    }
  }

  return (
    <form
      onSubmit={handleSubmit}
      noValidate
      aria-label={`Edit ${user.username}`}
      data-testid="edit-user-form"
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

      <Field id="edit-username" label="Username" hint="A username cannot be changed.">
        {(control) => <input {...control} name="username" type="text" value={user.username} readOnly />}
      </Field>

      <Field id="edit-fullName" label="Full Name" required error={fieldErrors.fullname}>
        {(control) => (
          <input
            {...control}
            name="fullName"
            type="text"
            autoComplete="off"
            value={fullName}
            onChange={(event) => {
              setFullName(event.target.value);
              clearError('fullname');
            }}
            disabled={saving}
          />
        )}
      </Field>

      {isDoctor && (
        <Field
          id="edit-roomNumber"
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
                clearError('roomnumber');
              }}
              disabled={saving}
            />
          )}
        </Field>
      )}

      {isDoctor && (
        <Field id="edit-specialization" label="Specialization" optional error={fieldErrors.specialization}>
          {(control) => (
            <input
              {...control}
              name="specialization"
              type="text"
              autoComplete="off"
              maxLength={64}
              value={specialization}
              onChange={(event) => {
                setSpecialization(event.target.value);
                clearError('specialization');
              }}
              disabled={saving}
            />
          )}
        </Field>
      )}

      <div className="flex flex-wrap gap-2 sm:col-span-2">
        <Button type="submit" size="sm" loading={saving}>
          {saving ? 'Saving…' : 'Save Changes'}
        </Button>
        <Button variant="secondary" size="sm" disabled={saving} onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  );
}
