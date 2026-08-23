import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from './useAuth';
import { roleRoutes } from './roleRoutes';
import type { UserRole } from './types';

interface ProtectedRouteProps {
  allowedRole: UserRole;
  children: ReactNode;
}

export function ProtectedRoute({ allowedRole, children }: ProtectedRouteProps) {
  const { isAuthenticated, user } = useAuth();

  if (!isAuthenticated || !user) {
    return <Navigate to="/login" replace />;
  }

  if (user.role !== allowedRole) {
    return <Navigate to={roleRoutes[user.role]} replace />;
  }

  return <>{children}</>;
}
