import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { recordVitalSigns } from '../api/consultations';
import { VitalSignsForm } from './VitalSignsForm';

vi.mock('../api/consultations', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api/consultations')>();
  return {
    ...original,
    recordVitalSigns: vi.fn(),
  };
});

const recordVitalSignsMock = vi.mocked(recordVitalSigns);

describe('VitalSignsForm', () => {
  beforeEach(() => {
    recordVitalSignsMock.mockReset();
  });

  afterEach(() => {
    cleanup();
  });

  it('calculates BMI while height and weight are entered and leaves it empty when either is missing', () => {
    render(<VitalSignsForm consultationId="consultation-1" />);

    const bmi = screen.getByLabelText(/^BMI/);
    fireEvent.change(screen.getByLabelText(/^Height/), { target: { value: '175' } });
    expect(bmi).toHaveTextContent('');

    fireEvent.change(screen.getByLabelText(/^Weight/), { target: { value: '70' } });
    expect(bmi).toHaveTextContent('22.86');

    fireEvent.change(screen.getByLabelText(/^Height/), { target: { value: '' } });
    expect(bmi).toHaveTextContent('');
  });

  it('warns about an unusual value without blocking submission', async () => {
    recordVitalSignsMock.mockResolvedValue({
      id: 'vitals-1',
      consultationId: 'consultation-1',
      systolicBloodPressure: null,
      diastolicBloodPressure: null,
      temperatureCelsius: 50,
      pulseRate: null,
      respiratoryRate: null,
      oxygenSaturation: null,
      heightCentimeters: null,
      weightKilograms: null,
      bmi: null,
      recordedAt: '2026-09-18T09:30:00Z',
    });
    render(<VitalSignsForm consultationId="consultation-1" />);

    fireEvent.change(screen.getByLabelText(/^Temperature/), { target: { value: '50' } });

    expect(screen.getByText('Please verify this value')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Save Vitals' }));

    await waitFor(() => {
      expect(recordVitalSignsMock).toHaveBeenCalledWith('consultation-1', {
        systolicBloodPressure: null,
        diastolicBloodPressure: null,
        temperatureCelsius: 50,
        pulseRate: null,
        respiratoryRate: null,
        oxygenSaturation: null,
        heightCentimeters: null,
        weightKilograms: null,
      });
    });
    expect(
      await screen.findByText('Measurements were linked to this consultation.'),
    ).toBeInTheDocument();
  });
});
