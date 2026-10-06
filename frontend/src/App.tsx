import { Navigate, Route, Routes } from 'react-router-dom';
import { LoginPage } from './pages/LoginPage';
import { UserManagementPage } from './pages/UserManagementPage';
import { AuditLogPage } from './pages/AuditLogPage';
import { PatientRegistrationPage } from './pages/PatientRegistrationPage';
import { PatientSearchPage } from './pages/PatientSearchPage';
import { PatientProfilePage } from './pages/PatientProfilePage';
import { PatientHistoryPage } from './pages/PatientHistoryPage';
import { QueueManagementPage } from './pages/QueueManagementPage';
import { WaitingRoomDisplayPage } from './pages/WaitingRoomDisplayPage';
import { ConsultationPage } from './pages/ConsultationPage';
import { PrescriptionPage } from './pages/PrescriptionPage';
import { PrescriptionDetailsPage } from './pages/PrescriptionDetailsPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { DoctorDashboard } from './dashboards/DoctorDashboard';
import { ReceptionistDashboard } from './dashboards/ReceptionistDashboard';
import { AdminDashboard } from './dashboards/AdminDashboard';

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/queue/display" element={<WaitingRoomDisplayPage />} />
      <Route
        path="/doctor"
        element={
          <ProtectedRoute allowedRole="Doctor">
            <DoctorDashboard />
          </ProtectedRoute>
        }
      />
      <Route
        path="/doctor/consultation"
        element={
          <ProtectedRoute allowedRole="Doctor">
            <ConsultationPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/doctor/prescription"
        element={
          <ProtectedRoute allowedRole="Doctor">
            <PrescriptionPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/prescriptions/queue/:queueId"
        element={
          <ProtectedRoute allowedRole={['Doctor', 'Receptionist', 'Admin']}>
            <PrescriptionDetailsPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/reception"
        element={
          <ProtectedRoute allowedRole="Receptionist">
            <ReceptionistDashboard />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin"
        element={
          <ProtectedRoute allowedRole="Admin">
            <AdminDashboard />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin/users"
        element={
          <ProtectedRoute allowedRole="Admin">
            <UserManagementPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/admin/audit-log"
        element={
          <ProtectedRoute allowedRole="Admin">
            <AuditLogPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/reception/patients/new"
        element={
          <ProtectedRoute allowedRole="Receptionist">
            <PatientRegistrationPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/reception/queue"
        element={
          <ProtectedRoute allowedRole="Receptionist">
            <QueueManagementPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/patients/search"
        element={
          <ProtectedRoute allowedRole={['Doctor', 'Receptionist', 'Admin']}>
            <PatientSearchPage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/patients/:patientId"
        element={
          <ProtectedRoute allowedRole={['Doctor', 'Receptionist', 'Admin']}>
            <PatientProfilePage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/patients/:patientId/history"
        element={
          <ProtectedRoute allowedRole="Doctor">
            <PatientHistoryPage />
          </ProtectedRoute>
        }
      />
      <Route path="/" element={<Navigate to="/login" replace />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}

export default App;
