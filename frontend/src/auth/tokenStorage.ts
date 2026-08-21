// Raw token lives in sessionStorage, not localStorage: on a shared clinical
// workstation, closing the tab must end the session rather than leaving a
// usable token behind for the next person to sit down.
const TOKEN_STORAGE_KEY = 'swiftcare.auth.token';

export function getStoredToken(): string | null {
  return sessionStorage.getItem(TOKEN_STORAGE_KEY);
}

export function setStoredToken(token: string): void {
  sessionStorage.setItem(TOKEN_STORAGE_KEY, token);
}

export function clearStoredToken(): void {
  sessionStorage.removeItem(TOKEN_STORAGE_KEY);
}
