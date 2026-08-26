import { Navigate, Route, Routes } from 'react-router-dom';
import { LoginPage } from './pages/LoginPage';
import { UserManagementPage } from './pages/UserManagementPage';
import { PatientRegistrationPage } from './pages/PatientRegistrationPage';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { DoctorDashboard } from './dashboards/DoctorDashboard';
import { ReceptionistDashboard } from './dashboards/ReceptionistDashboard';
import { AdminDashboard } from './dashboards/AdminDashboard';

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/doctor"
        element={
          <ProtectedRoute allowedRole="Doctor">
            <DoctorDashboard />
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
        path="/reception/patients/new"
        element={
          <ProtectedRoute allowedRole="Receptionist">
            <PatientRegistrationPage />
          </ProtectedRoute>
        }
      />
      <Route path="*" element={<Navigate to="/login" replace />} />
    </Routes>
  );
}

export default App;
