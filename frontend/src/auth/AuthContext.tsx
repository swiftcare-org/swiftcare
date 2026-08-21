import { useCallback, useMemo, useState, type ReactNode } from 'react';
import type { AuthenticatedUser } from './types';
import { clearStoredToken, getStoredToken, setStoredToken } from './tokenStorage';
import { AuthContext, type AuthContextValue } from './authContextInstance';

const USER_STORAGE_KEY = 'swiftcare.auth.user';

function readStoredUser(): AuthenticatedUser | null {
  const raw = sessionStorage.getItem(USER_STORAGE_KEY);
  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw) as AuthenticatedUser;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(() => getStoredToken());
  const [user, setUser] = useState<AuthenticatedUser | null>(() => readStoredUser());

  const signIn = useCallback((newToken: string, newUser: AuthenticatedUser) => {
    setStoredToken(newToken);
    sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(newUser));
    setToken(newToken);
    setUser(newUser);
  }, []);

  const signOut = useCallback(() => {
    clearStoredToken();
    sessionStorage.removeItem(USER_STORAGE_KEY);
    setToken(null);
    setUser(null);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAuthenticated: token !== null && user !== null,
      signIn,
      signOut,
    }),
    [user, token, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
