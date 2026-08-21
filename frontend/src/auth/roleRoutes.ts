import type { UserRole } from './types';

export const roleRoutes: Record<UserRole, string> = {
  Doctor: '/doctor',
  Receptionist: '/reception',
  Admin: '/admin',
};
