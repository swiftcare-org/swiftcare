import { useMemo, useState, type FormEvent } from 'react';
import {
  recordVitalSigns,
  type RecordVitalSignsRequestBody,
  type VitalSigns,
} from '../api/consultations';
import { ApiError } from '../api/client';

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

function inputClassName(hasError: boolean): string {
  return `mt-1.5 block w-full border-2 bg-white px-3 py-2.5 text-sm text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:bg-slate-100 disabled:text-slate-400 ${
    hasError ? 'border-red-600' : 'border-slate-400 focus:border-brand-blue'
  }`;
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
      fieldErrors[field] = 'Enter a value greater than zero';
    }
  }

  const hasSystolic = form.systolicBloodPressure.trim() !== '';
  const hasDiastolic = form.diastolicBloodPressure.trim() !== '';
  if (hasSystolic !== hasDiastolic) {
    const message = 'Enter both systolic and diastolic values';
    fieldErrors.systolicBloodPressure = message;
    fieldErrors.diastolicBloodPressure = message;
  }

  return {
    fieldErrors,
    formError: hasAnyMeasurement ? null : 'Enter at least one vital sign',
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
  const descriptionIds = [
    error ? `${field}-error` : null,
    warning ? `${field}-warning` : null,
  ].filter(Boolean).join(' ') || undefined;

  return (
    <div>
      <label htmlFor={field} className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
        {label} <span className="font-normal normal-case tracking-normal text-slate-500">({unit})</span>
      </label>
      <input
        id={field}
        name={field}
        type="number"
        inputMode="decimal"
        step={step}
        value={value}
        onChange={(event) => onChange(field, event.target.value)}
        disabled={disabled}
        aria-invalid={error ? true : undefined}
        aria-describedby={descriptionIds}
        className={inputClassName(!!error)}
      />
      {error && (
        <p id={`${field}-error`} className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
          {error}
        </p>
      )}
      {warning && (
        <p id={`${field}-warning`} className="mt-1 border-l-2 border-amber-600 pl-2 text-xs font-medium text-amber-800">
          Please verify this value
        </p>
      )}
    </div>
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

  return (
    <section className="mt-6 border border-slate-300 bg-white px-6 py-6">
      <div className="border-b border-slate-300 pb-4">
        <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-brand-blue-dark">
          Visit Measurements
        </p>
        <h2 className="mt-1 text-xl font-semibold text-slate-900">Record Vital Signs</h2>
        <p className="mt-1 text-sm text-slate-600">
          Enter available measurements. Unusual positive values can still be saved after verification.
        </p>
      </div>

      <div aria-live="polite">
        {isSaved && savedVitalSigns && (
          <div className="mt-5 border-t-4 border-b border-emerald-700 bg-emerald-50 px-5 py-3">
            <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-emerald-800">
              Vital Signs Saved
            </p>
            <p className="mt-1 text-sm text-emerald-900">
              Measurements were linked to this consultation
              {savedVitalSigns.bmi === null ? '.' : ` with a BMI of ${savedVitalSigns.bmi.toFixed(2)}.`}
            </p>
          </div>
        )}
        {isSaved && !savedVitalSigns && (
          <div className="mt-5 border-t-4 border-b border-emerald-700 bg-emerald-50 px-5 py-3">
            <p className="text-sm text-emerald-900">Vital signs were saved for this consultation.</p>
          </div>
        )}

        {saveState === 'failed' && message && (
          <div className="mt-5 border-t-4 border-b border-red-700 bg-red-50 px-5 py-3" role="alert">
            <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">
              Vital Signs Not Saved
            </p>
            <p className="mt-1 text-sm text-red-900">{message}</p>
          </div>
        )}
      </div>

      <form onSubmit={handleSubmit} noValidate className="mt-5 space-y-5">
        {formError && (
          <p className="border-l-2 border-red-600 bg-red-50 px-3 py-2 text-sm font-medium text-red-700" role="alert">
            {formError}
          </p>
        )}

        <fieldset disabled={isBusy || isSaved} className="space-y-5">
          <legend className="sr-only">Vital sign measurements</legend>

          <div>
            <p className="text-xs font-bold uppercase tracking-[0.12em] text-slate-600">Blood Pressure (mmHg)</p>
            <div className="mt-2 grid gap-4 sm:grid-cols-2">
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
          </div>

          <div>
            <label htmlFor="bmi" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
              BMI <span className="font-normal normal-case tracking-normal text-slate-500">(kg/m²)</span>
            </label>
            <output
              id="bmi"
              htmlFor="heightCentimeters weightKilograms"
              className="mt-1.5 block min-h-11 w-full border-2 border-slate-300 bg-slate-100 px-3 py-2.5 text-sm font-semibold text-slate-900"
            >
              {bmi}
            </output>
            <p className="mt-1 text-xs text-slate-500">
              Calculated automatically when height and weight are provided.
            </p>
          </div>
        </fieldset>

        <button
          type="submit"
          disabled={isBusy || isSaved}
          className="relative w-full overflow-hidden bg-brand-blue px-4 py-3 text-sm font-bold uppercase tracking-[0.15em] text-white hover:bg-brand-blue-dark focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {isBusy ? 'Saving...' : isSaved ? 'Vital Signs Saved' : 'Save Vitals'}
          {isBusy && (
            <span className="absolute inset-x-0 bottom-0 block h-0.5 overflow-hidden bg-white/20" aria-hidden="true">
              <span className="block h-full w-1/3 animate-[loading-sweep_1.1s_ease-in-out_infinite] bg-white" />
            </span>
          )}
        </button>
      </form>
    </section>
  );
}
