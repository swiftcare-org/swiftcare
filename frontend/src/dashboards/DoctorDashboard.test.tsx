import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { getAllergies } from '../api/allergies';
import { getConditions } from '../api/conditions';
import { getPatient } from '../api/patients';
import { getWaitingPool } from '../api/queue';
import { AuthContext, type AuthContextValue } from '../auth/authContextInstance';
import type { AuthenticatedUser } from '../auth/types';
import {
  storeCurrentPatient,
  type CurrentPatient,
} from '../consultations/currentPatientStorage';

vi.mock('../api/allergies', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/allergies')>()),
  getAllergies: vi.fn(),
}));

vi.mock('../api/conditions', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/conditions')>()),
  getConditions: vi.fn(),
}));

vi.mock('../api/patients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/patients')>()),
  getPatient: vi.fn(),
}));

vi.mock('../api/queue', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/queue')>()),
  getWaitingPool: vi.fn(),
}));

const doctor: AuthenticatedUser = {
  userId: 'doctor-1',
  fullName: 'Dr. Amara Chen',
  role: 'Doctor',
  roomNumber: 'R-204',
};

const currentPatient: CurrentPatient = {
  queueId: 'queue-17',
  patientId: 'patient-17',
  queueNumber: 'Q-017',
  status: 'IN_CONSULTATION',
  doctorId: doctor.userId,
  doctorName: doctor.fullName,
  roomNumber: doctor.roomNumber!,
  calledAt: '2026-09-18T08:30:00Z',
  patientName: 'Nimal Perera',
};

const authValue: AuthContextValue = {
  user: doctor,
  isAuthenticated: true,
  signIn: vi.fn(),
  signOut: vi.fn(),
};

function renderApp() {
  return render(
    <MemoryRouter initialEntries={['/doctor']}>
      <AuthContext.Provider value={authValue}>
        <App />
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

describe('SWC-105 current patient profile link coverage', () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.clearAllMocks();

    vi.mocked(getWaitingPool).mockResolvedValue([]);
    vi.mocked(getPatient).mockResolvedValue({
      patientId: currentPatient.patientId,
      fullName: currentPatient.patientName,
      nic: '901234567V',
      dateOfBirth: '1990-05-20',
      gender: 'Male',
      address: '17 Lake Road, Colombo',
      phoneNumber: '0771234567',
      bloodGroup: 'O+',
      registeredAt: '2026-09-01T06:00:00Z',
    });
    vi.mocked(getAllergies).mockResolvedValue([
      {
        allergyId: 'allergy-1',
        allergyName: 'Penicillin',
        severity: 'Severe',
        notes: 'Causes swelling',
        recordedAt: '2026-09-02T06:00:00Z',
      },
    ]);
    vi.mocked(getConditions).mockResolvedValue([
      {
        conditionId: 'condition-1',
        conditionName: 'Asthma',
        dateDiagnosed: '2020-03-10',
        notes: 'Uses an inhaler',
      },
    ]);
  });

  it.each([currentPatient.queueNumber, currentPatient.patientName])(
    'opens the doctor-readable patient profile from the %s link',
    async (linkName) => {
      storeCurrentPatient(doctor.userId, currentPatient);
      const user = userEvent.setup();
      renderApp();

      await user.click(screen.getByRole('link', { name: linkName }));

      expect(await screen.findByRole('heading', { name: 'Patient Profile' })).toBeInTheDocument();
      expect(await screen.findByText('901234567V')).toBeInTheDocument();
      expect(screen.getByText('17 Lake Road, Colombo')).toBeInTheDocument();
      expect(screen.getByRole('cell', { name: 'Penicillin' })).toBeInTheDocument();
      expect(screen.getByRole('cell', { name: 'Asthma' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Edit Profile' })).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Address')).not.toBeInTheDocument();
      expect(getPatient).toHaveBeenCalledWith(currentPatient.patientId);
      expect(getAllergies).toHaveBeenCalledWith(currentPatient.patientId);
      expect(getConditions).toHaveBeenCalledWith(currentPatient.patientId);
    },
  );

  it('removes both profile links after the current-patient assignment is cleared', () => {
    storeCurrentPatient(doctor.userId, currentPatient);
    const firstRender = renderApp();

    expect(
      screen.getByRole('link', { name: currentPatient.queueNumber }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: currentPatient.patientName }),
    ).toBeInTheDocument();

    firstRender.unmount();
    sessionStorage.clear();
    renderApp();

    expect(
      screen.queryByRole('link', { name: currentPatient.queueNumber }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: currentPatient.patientName }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText('Current Consultation')).not.toBeInTheDocument();
  });
});
