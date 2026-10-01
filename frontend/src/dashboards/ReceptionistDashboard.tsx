import { ActionTile, ActionTileGrid } from '../components/ui/ActionTile';
import { DashboardShell } from './DashboardShell';

export function ReceptionistDashboard() {
  return (
    <DashboardShell sectionLabel="Receptionist Dashboard">
      <ActionTileGrid label="Reception actions">
        <ActionTile to="/reception/patients/new" title="Register Patient" description="Add a new patient and check them in." />
        <ActionTile to="/patients/search" title="Search Patients" description="Find a patient by name, NIC or phone number." />
        <ActionTile to="/reception/queue" title="View Today's Queue" description="See every patient's status and pending prescriptions." />
      </ActionTileGrid>
    </DashboardShell>
  );
}
