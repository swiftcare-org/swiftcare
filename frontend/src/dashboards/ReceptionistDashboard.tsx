import { ActionTile, ActionTileGrid } from '../components/ui/ActionTile';
import { DashboardShell } from './DashboardShell';

export function ReceptionistDashboard() {
  return (
    <DashboardShell sectionLabel="Receptionist Dashboard">
      <ActionTileGrid label="Reception actions">
        <ActionTile icon="userPlus" to="/reception/patients/new" title="Register Patient" description="New patient record" />
        <ActionTile icon="search" to="/patients/search" title="Search Patients" description="Name, NIC or phone" />
        <ActionTile icon="queue" to="/reception/queue" title="Today's Queue" description="Patient status" />
      </ActionTileGrid>
    </DashboardShell>
  );
}
