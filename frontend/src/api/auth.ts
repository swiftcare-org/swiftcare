import { apiRequest } from './client';
import type { LoginResponseBody } from '../auth/types';

export interface LoginCredentials {
  username: string;
  password: string;
}

export function login(credentials: LoginCredentials): Promise<LoginResponseBody> {
  return apiRequest<LoginResponseBody>('/api/auth/login', {
    method: 'POST',
    body: credentials,
  });
}

export function logout(): Promise<void> {
  return apiRequest<void>('/api/auth/logout', {
    method: 'POST',
  });
}
