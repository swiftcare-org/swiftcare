import { apiRequest } from './client';
import type { UserRole } from '../auth/types';

export interface CreateUserRequestBody {
  username: string;
  password: string;
  fullName: string;
  role: UserRole;
  // Required for Doctor accounts only.
  roomNumber?: string;
}

export interface UserSummary {
  userId: string;
  username: string;
  fullName: string;
  role: UserRole;
  // Present for Doctor accounts only.
  roomNumber?: string;
  isActive: boolean;
  createdAt: string;
}

export function createUser(request: CreateUserRequestBody): Promise<UserSummary> {
  return apiRequest<UserSummary>('/api/users', {
    method: 'POST',
    body: request,
  });
}

export function listUsers(): Promise<UserSummary[]> {
  return apiRequest<UserSummary[]>('/api/users');
}
