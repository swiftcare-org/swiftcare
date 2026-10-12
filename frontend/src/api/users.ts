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

// The username and role are fixed once an account exists, so they are not part of an edit.
export interface UpdateUserRequestBody {
  fullName: string;
  // Required for Doctor accounts only.
  roomNumber?: string;
  // Doctor accounts only.
  specialization?: string;
}

export interface UserSummary {
  userId: string;
  username: string;
  fullName: string;
  role: UserRole;
  // Present for Doctor accounts only.
  roomNumber?: string;
  // Optional, and present for Doctor accounts only.
  specialization?: string;
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

export function updateUser(userId: string, request: UpdateUserRequestBody): Promise<UserSummary> {
  return apiRequest<UserSummary>(`/api/users/${encodeURIComponent(userId)}`, {
    method: 'PUT',
    body: request,
  });
}

export function resetUserPassword(userId: string, newPassword: string): Promise<UserSummary> {
  return apiRequest<UserSummary>(`/api/users/${encodeURIComponent(userId)}/reset-password`, {
    method: 'PUT',
    body: { newPassword },
  });
}

export function deactivateUser(userId: string): Promise<UserSummary> {
  return apiRequest<UserSummary>(`/api/users/${encodeURIComponent(userId)}/deactivate`, { method: 'PUT' });
}

export function reactivateUser(userId: string): Promise<UserSummary> {
  return apiRequest<UserSummary>(`/api/users/${encodeURIComponent(userId)}/activate`, { method: 'PUT' });
}
