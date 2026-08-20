import { createContext } from 'react';
import type { AuthenticatedUser } from './types';

export interface AuthContextValue {
  user: AuthenticatedUser | null;
  isAuthenticated: boolean;
  signIn: (token: string, user: AuthenticatedUser) => void;
  signOut: () => void;
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined);
