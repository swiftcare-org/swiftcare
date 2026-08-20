export type UserRole = 'Doctor' | 'Receptionist' | 'Admin';

export interface AuthenticatedUser {
  userId: string;
  fullName: string;
  role: UserRole;
  // Present for Doctor accounts only.
  roomNumber?: string;
}

export interface LoginResponseBody {
  token: string;
  expiresAt: string;
  user: AuthenticatedUser;
}
