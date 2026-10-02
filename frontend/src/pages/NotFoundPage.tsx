import { Navigate } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';
import { DashboardShell } from '../dashboards/DashboardShell';
import { ButtonLink } from '../components/ui/Button';
import { SectionCard } from '../components/ui/SectionCard';

export function NotFoundPage() {
  const { user } = useAuth();

  // Someone who is not signed in has nowhere else to go, so they land on the sign-in page.
  if (!user) {
    return <Navigate to="/login" replace />;
  }

  return (
    <DashboardShell sectionLabel="Page Not Found">
      <SectionCard
        title="This page does not exist."
        description="The address may be mistyped, or the page may have been moved."
      >
        <ButtonLink to={roleRoutes[user.role]}>Go to Dashboard</ButtonLink>
      </SectionCard>
    </DashboardShell>
  );
}
