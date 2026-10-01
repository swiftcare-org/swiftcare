import { useMemo, useState, type FormEvent } from 'react';
import {
  recordVitalSigns,
  type RecordVitalSignsRequestBody,
  type VitalSigns,
} from '../api/consultations';
import { ApiError } from '../api/client';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { Field } from '../components/ui/Field';
import { labelClassName } from '../components/ui/fieldStyles';
import { SectionCard } from '../components/ui/SectionCard';

type SaveState = 'idle' | 'saving' | 'saved' | 'failed';

interface VitalSignsFormState {
  systolicBloodPressure: string;
  diastolicBloodPressure: string;
  temperatureCelsius: string;
  pulseRate: string;
  respiratoryRate: string;
  oxygenSaturation: string;
  heightCentimeters: string;
  weightKilograms: string;
}

type VitalSignsField = keyof VitalSignsFormState;
type FieldErrors = Partial<Record<VitalSignsField, string>>;

interface VitalSignsFormProps {
  consultationId: string;
  alreadySaved?: boolean;
  onSaved: () => void;
}

const EMPTY_FORM: VitalSignsFormState = {
  systolicBloodPressure: '',
  diastolicBloodPressure: '',
  temperatureCelsius: '',
  pulseRate: '',
  respiratoryRate: '',
  oxygenSaturation: '',
  heightCentimeters: '',
  weightKilograms: '',
};

const FIELD_NAMES: VitalSignsField[] = [
  'systolicBloodPressure',
  'diastolicBloodPressure',
  'temperatureCelsius',
  'pulseRate',
  'respiratoryRate',
  'oxygenSaturation',
  'heightCentimeters',
  'weightKilograms',
];

function optionalNumber(value: string): number | null {
  const trimmed = value.trim();
  return trimmed ? Number(trimmed) : null;
}

function calculateBmi(
  heightCentimeters: string,
  weightKilograms: string,
): string {
  const height = optionalNumber(heightCentimeters);
  const weight = optionalNumber(weightKilograms);
  if (height === null || weight === null || height <= 0 || weight <= 0) {
    return '';
  }

  const heightMeters = height / 100;
  return (weight / (heightMeters * heightMeters)).toFixed(2);
}

function isOutsideRange(value: string, minimum: number, maximum: number): boolean {
  const measurement = optionalNumber(value);
  return measurement !== null
    && measurement > 0
    && (measurement < minimum || measurement > maximum);
}

function vitalSignsErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401 || error.status === 403) {
      return 'You are not authorized to record vital signs.';
    }

    if (error.status === 404 || error.status === 409) {
      return error.message;
    }
  }

  return 'Unable to save vital signs. Please try again.';
}

function validate(form: VitalSignsFormState): {
  fieldErrors: FieldErrors;
  formError: string | null;
} {
  const fieldErrors: FieldErrors = {};
  const hasAnyMeasurement = FIELD_NAMES.some((field) => form[field].trim());

  for (const field of FIELD_NAMES) {
    const value = optionalNumber(form[field]);
    if (value !== null && (!Number.isFinite(value) || value <= 0)) {
      fieldErrors[field] = 'Enter a value greater than zero.';
    }
  }

  const hasSystolic = form.systolicBloodPressure.trim() !== '';
  const hasDiastolic = form.diastolicBloodPressure.trim() !== '';
  if (hasSystolic !== hasDiastolic) {
    const message = 'Enter both systolic and diastolic values.';
    fieldErrors.systolicBloodPressure = message;
    fieldErrors.diastolicBloodPressure = message;
  }

  return {
    fieldErrors,
    formError: hasAnyMeasurement ? null : 'Enter at least one vital sign.',
  };
}

function toRequest(form: VitalSignsFormState): RecordVitalSignsRequestBody {
  return {
    systolicBloodPressure: optionalNumber(form.systolicBloodPressure),
    diastolicBloodPressure: optionalNumber(form.diastolicBloodPressure),
    temperatureCelsius: optionalNumber(form.temperatureCelsius),
    pulseRate: optionalNumber(form.pulseRate),
    respiratoryRate: optionalNumber(form.respiratoryRate),
    oxygenSaturation: optionalNumber(form.oxygenSaturation),
    heightCentimeters: optionalNumber(form.heightCentimeters),
    weightKilograms: optionalNumber(form.weightKilograms),
  };
}

interface MeasurementFieldProps {
  field: VitalSignsField;
  label: string;
  unit: string;
  value: string;
  step?: string;
  error?: string;
  warning: boolean;
  disabled: boolean;
  onChange: (field: VitalSignsField, value: string) => void;
}

function MeasurementField({
  field,
  label,
  unit,
  value,
  step = '1',
  error,
  warning,
  disabled,
  onChange,
}: MeasurementFieldProps) {
  return (
    <Field
      id={field}
      label={
        <>
          {label} <span className="font-normal text-slate-400">({unit})</span>
        </>
      }
      error={error}
      warning={warning ? 'Please verify this value' : null}
    >
      {(control) => (
        <input
          {...control}
          name={field}
          type="number"
          inputMode="decimal"
          step={step}
          value={value}
          onChange={(event) => onChange(field, event.target.value)}
          disabled={disabled}
        />
      )}
    </Field>
  );
}

export function VitalSignsForm({ consultationId, alreadySaved = false, onSaved }: VitalSignsFormProps) {
  const [form, setForm] = useState<VitalSignsFormState>(EMPTY_FORM);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saveState, setSaveState] = useState<SaveState>(alreadySaved ? 'saved' : 'idle');
  const [message, setMessage] = useState<string | null>(null);
  const [savedVitalSigns, setSavedVitalSigns] = useState<VitalSigns | null>(null);

  const bmi = useMemo(
    () => calculateBmi(form.heightCentimeters, form.weightKilograms),
    [form.heightCentimeters, form.weightKilograms],
  );
  const isBusy = saveState === 'saving';
  const isSaved = saveState === 'saved';

  function handleFieldChange(field: VitalSignsField, value: string) {
    setForm((previous) => ({ ...previous, [field]: value }));
    setFieldErrors((previous) => {
      if (!previous[field]) {
        return previous;
      }

      const next = { ...previous };
      delete next[field];
      return next;
    });
    setFormError(null);
    if (saveState === 'failed') {
      setSaveState('idle');
      setMessage(null);
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const validation = validate(form);
    setFieldErrors(validation.fieldErrors);
    setFormError(validation.formError);

    if (validation.formError || Object.keys(validation.fieldErrors).length > 0) {
      return;
    }

    setSaveState('saving');
    setMessage(null);

    try {
      const vitalSigns = await recordVitalSigns(consultationId, toRequest(form));
      setSavedVitalSigns(vitalSigns);
      setSaveState('saved');
      onSaved();
    } catch (error) {
      setSavedVitalSigns(null);
      setSaveState('failed');
      setMessage(vitalSignsErrorMessage(error));

      if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
        const serverFieldErrors: FieldErrors = {};
        for (const field of FIELD_NAMES) {
          const serverError = error.fieldErrors[field.toLowerCase()];
          if (serverError) {
            serverFieldErrors[field] = serverError;
          }
        }
        setFieldErrors(serverFieldErrors);
      }
    }
  }

  // Saved in an earlier session: there are no values to show, so the locked, empty
  // inputs are replaced by a plain statement of what happened.
  const savedEarlier = isSaved && !savedVitalSigns;

  return (
    <SectionCard
      eyebrow="Step 2"
      title="Record Vital Signs"
      description="Every measurement is optional, but at least one is needed. Unusual values can still be saved after you verify them."
    >
      <div aria-live="polite" className="space-y-3">
        {isSaved && savedVitalSigns && (
          <Banner tone="success" title="Vital Signs Saved">
            <p>
              Measurements were linked to this consultation
              {savedVitalSigns.bmi === null ? '.' : ` with a BMI of ${savedVitalSigns.bmi.toFixed(2)}.`}
            </p>
          </Banner>
        )}

        {savedEarlier && (
          <Banner tone="success" title="Vital Signs Saved">
            Vital signs were saved for this consultation earlier and can no longer be edited here.
          </Banner>
        )}

        {saveState === 'failed' && message && (
          <Banner tone="error" title="Vital Signs Not Saved" role="alert">
            {message}
          </Banner>
        )}
      </div>

      {!savedEarlier && (
        <form onSubmit={handleSubmit} noValidate className="mt-5 space-y-5">
          {formError && (
            <Banner tone="error" role="alert">
              {formError}
            </Banner>
          )}

          <fieldset disabled={isBusy || isSaved} className="space-y-5">
            <legend className="sr-only">Vital sign measurements</legend>

            <div>
              <p className={labelClassName}>Blood Pressure</p>
              <div className="mt-2 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
                <MeasurementField
                  field="systolicBloodPressure"
                  label="Systolic"
                  unit="mmHg"
                  value={form.systolicBloodPressure}
                  error={fieldErrors.systolicBloodPressure}
                  warning={isOutsideRange(form.systolicBloodPressure, 90, 120)}
                  disabled={isBusy || isSaved}
                  onChange={handleFieldChange}
                />
                <MeasurementField
                  field="diastolicBloodPressure"
                  label="Diastolic"
                  unit="mmHg"
                  value={form.diastolicBloodPressure}
                  error={fieldErrors.diastolicBloodPressure}
                  warning={isOutsideRange(form.diastolicBloodPressure, 60, 80)}
                  disabled={isBusy || isSaved}
                  onChange={handleFieldChange}
                />
              </div>
            </div>

            <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
              <MeasurementField
                field="temperatureCelsius"
                label="Temperature"
                unit="°C"
                step="0.1"
                value={form.temperatureCelsius}
                error={fieldErrors.temperatureCelsius}
                warning={isOutsideRange(form.temperatureCelsius, 36.1, 37.2)}
                disabled={isBusy || isSaved}
                onChange={handleFieldChange}
              />
              <MeasurementField
                field="pulseRate"
                label="Pulse Rate"
                unit="bpm"
                value={form.pulseRate}
                error={fieldErrors.pulseRate}
                warning={isOutsideRange(form.pulseRate, 60, 100)}
                disabled={isBusy || isSaved}
                onChange={handleFieldChange}
              />
              <MeasurementField
                field="respiratoryRate"
                label="Respiratory Rate"
                unit="breaths/min"
                value={form.respiratoryRate}
                error={fieldErrors.respiratoryRate}
                warning={isOutsideRange(form.respiratoryRate, 12, 20)}
                disabled={isBusy || isSaved}
                onChange={handleFieldChange}
              />
              <MeasurementField
                field="oxygenSaturation"
                label="Oxygen Saturation"
                unit="%"
                value={form.oxygenSaturation}
                error={fieldErrors.oxygenSaturation}
                warning={isOutsideRange(form.oxygenSaturation, 95, 100)}
                disabled={isBusy || isSaved}
                onChange={handleFieldChange}
              />
              <MeasurementField
                field="heightCentimeters"
                label="Height"
                unit="cm"
                step="0.01"
                value={form.heightCentimeters}
                error={fieldErrors.heightCentimeters}
                warning={false}
                disabled={isBusy || isSaved}
                onChange={handleFieldChange}
              />
              <MeasurementField
                field="weightKilograms"
                label="Weight"
                unit="kg"
                step="0.01"
                value={form.weightKilograms}
                error={fieldErrors.weightKilograms}
                warning={false}
                disabled={isBusy || isSaved}
                onChange={handleFieldChange}
              />
              <div>
                <label htmlFor="bmi" className={labelClassName}>
                  BMI <span className="font-normal text-slate-400">(kg/m²)</span>
                </label>
                <output
                  id="bmi"
                  htmlFor="heightCentimeters weightKilograms"
                  className="mt-1.5 block min-h-[2.375rem] w-full rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm font-semibold text-slate-900"
                >
                  {bmi}
                </output>
                <p className="mt-1 text-xs text-slate-500">Calculated from height and weight.</p>
              </div>
            </div>
          </fieldset>

          <Button type="submit" fullWidth loading={isBusy} disabled={isSaved}>
            {isBusy ? 'Saving…' : isSaved ? 'Vital Signs Saved' : 'Save Vitals'}
          </Button>
        </form>
      )}
    </SectionCard>
  );
}
