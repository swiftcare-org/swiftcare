import { ActionTile, ActionTileGrid } from '../components/ui/ActionTile';
import { DashboardShell } from './DashboardShell';

export function AdminDashboard() {
  return (
    <DashboardShell sectionLabel="Admin Dashboard">
      <ActionTileGrid label="Admin actions">
        <ActionTile icon="users" to="/admin/users" title="Manage Users" description="Staff accounts" />
        <ActionTile icon="search" to="/patients/search" title="Search Patients" description="Name, NIC or phone" />
      </ActionTileGrid>
    </DashboardShell>
  );
}
