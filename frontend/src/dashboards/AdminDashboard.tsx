import { ActionTile, ActionTileGrid } from '../components/ui/ActionTile';
import { DashboardShell } from './DashboardShell';

export function AdminDashboard() {
  return (
    <DashboardShell sectionLabel="Admin Dashboard">
      <ActionTileGrid label="Admin actions">
        <ActionTile to="/admin/users" title="Manage Users" description="Create staff accounts and review existing ones." />
        <ActionTile to="/patients/search" title="Search Patients" description="Find a patient by name, NIC or phone number." />
      </ActionTileGrid>
    </DashboardShell>
  );
}
