import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { login } from '../api/auth';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';
import swiftcareLogo from '../assets/swiftcare-logo.svg';

type SubmissionStatus = 'idle' | 'submitting' | 'rejected' | 'deactivated' | 'issued';

interface FieldErrors {
  username: string | null;
  password: string | null;
}

const GENERIC_ERROR_MESSAGE = 'Unable to sign in. Please try again.';
const REDIRECT_DELAY_MS = 600;

export function LoginPage() {
  const { signIn } = useAuth();
  const navigate = useNavigate();

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({ username: null, password: null });
  const [status, setStatus] = useState<SubmissionStatus>('idle');
  const [serverMessage, setServerMessage] = useState<string | null>(null);

  const isBusy = status === 'submitting' || status === 'issued';

  function handleUsernameChange(value: string) {
    setUsername(value);
    setFieldErrors((prev) => (prev.username ? { ...prev, username: null } : prev));
    if (status === 'rejected' || status === 'deactivated') {
      setStatus('idle');
      setServerMessage(null);
    }
  }

  function handlePasswordChange(value: string) {
    setPassword(value);
    setFieldErrors((prev) => (prev.password ? { ...prev, password: null } : prev));
    if (status === 'rejected' || status === 'deactivated') {
      setStatus('idle');
      setServerMessage(null);
    }
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    const trimmedUsername = username.trim();
    const trimmedPassword = password.trim();

    const nextFieldErrors: FieldErrors = {
      username: trimmedUsername ? null : 'Enter your username.',
      password: trimmedPassword ? null : 'Enter your password.',
    };
    setFieldErrors(nextFieldErrors);

    if (nextFieldErrors.username || nextFieldErrors.password) {
      // Client-side validation failure - no network request is made.
      return;
    }

    setStatus('submitting');
    setServerMessage(null);

    try {
      const result = await login({ username: trimmedUsername, password });
      signIn(result.token, result.user);
      setStatus('issued');

      window.setTimeout(() => {
        navigate(roleRoutes[result.user.role], { replace: true });
      }, REDIRECT_DELAY_MS);
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setStatus('rejected');
        setServerMessage('Invalid username or password');
      } else if (error instanceof ApiError && error.status === 403) {
        setStatus('deactivated');
        setServerMessage('Your account has been deactivated. Contact your administrator.');
      } else {
        setStatus('rejected');
        setServerMessage(GENERIC_ERROR_MESSAGE);
      }
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-slate-100 px-4 py-10">
      <div className="w-full max-w-md border border-slate-300 bg-white shadow-[4px_4px_0_rgba(15,23,42,0.08)]">
        {/* Identifier plate */}
        <div className="flex justify-center border-b border-slate-200 px-6 py-6">
          <img src={swiftcareLogo} alt="SwiftCare" className="h-10 w-auto" />
        </div>

        {/* Directional band */}
        <div className="bg-brand-blue px-6 py-2.5">
          <h1 className="text-center text-xs font-bold uppercase tracking-[0.22em] text-white">Staff Sign-In</h1>
        </div>

        {/* Status region - one persistent aria-live container, content swapped by status */}
        <div aria-live="polite">
          {status === 'rejected' && (
            <div className="border-t-4 border-b border-red-700 bg-red-50 px-6 py-3">
              <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-red-800">Access Denied</p>
              <p className="mt-1 text-sm text-red-900">{serverMessage}</p>
            </div>
          )}
          {status === 'deactivated' && (
            <div className="border-t-4 border-b border-amber-600 bg-amber-50 px-6 py-3">
              <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-amber-800">Account Deactivated</p>
              <p className="mt-1 text-sm text-amber-900">{serverMessage}</p>
            </div>
          )}
          {status === 'issued' && (
            <div className="border-t-4 border-b border-emerald-700 bg-emerald-50 px-6 py-3">
              <p className="text-[11px] font-bold uppercase tracking-[0.15em] text-emerald-800">Access Granted</p>
              <p className="mt-1 text-sm text-emerald-900">Redirecting to your dashboard…</p>
            </div>
          )}
        </div>

        <form onSubmit={handleSubmit} noValidate className="space-y-5 px-6 py-6">
          <div>
            <label htmlFor="username" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
              Username
            </label>
            <input
              id="username"
              name="username"
              type="text"
              autoComplete="username"
              value={username}
              onChange={(event) => handleUsernameChange(event.target.value)}
              disabled={isBusy}
              aria-invalid={fieldErrors.username ? true : undefined}
              aria-describedby={fieldErrors.username ? 'username-error' : undefined}
              className={`mt-1.5 block w-full border-2 bg-white px-3 py-2.5 text-sm text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:bg-slate-100 disabled:text-slate-400 ${
                fieldErrors.username ? 'border-red-600' : 'border-slate-400 focus:border-brand-blue'
              }`}
            />
            {fieldErrors.username && (
              <p id="username-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                {fieldErrors.username}
              </p>
            )}
          </div>

          <div>
            <label htmlFor="password" className="block text-xs font-bold uppercase tracking-[0.12em] text-slate-600">
              Password
            </label>
            <input
              id="password"
              name="password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(event) => handlePasswordChange(event.target.value)}
              disabled={isBusy}
              aria-invalid={fieldErrors.password ? true : undefined}
              aria-describedby={fieldErrors.password ? 'password-error' : undefined}
              className={`mt-1.5 block w-full border-2 bg-white px-3 py-2 text-sm text-slate-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 disabled:bg-slate-100 disabled:text-slate-400 ${
                fieldErrors.password ? 'border-red-600' : 'border-slate-300 focus:border-brand-blue'
              }`}
            />
            {fieldErrors.password && (
              <p id="password-error" className="mt-1 border-l-2 border-red-600 pl-2 text-xs font-medium text-red-700">
                {fieldErrors.password}
              </p>
            )}
          </div>

          <button
            type="submit"
            disabled={isBusy}
            className={`relative w-full overflow-hidden px-4 py-3 text-sm font-bold uppercase tracking-[0.15em] text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-blue focus-visible:ring-offset-2 ${
              status === 'submitting'
                ? 'bg-slate-900'
                : status === 'issued'
                  ? 'bg-emerald-700'
                  : 'bg-brand-blue hover:bg-brand-blue-dark'
            }`}
          >
            {status === 'submitting' ? 'Verifying…' : status === 'issued' ? 'Access Granted' : 'Sign In'}
            {status === 'submitting' && (
              <span className="absolute inset-x-0 bottom-0 block h-0.5 overflow-hidden bg-white/20" aria-hidden="true">
                <span className="block h-full w-1/3 animate-[loading-sweep_1.1s_ease-in-out_infinite] bg-white" />
              </span>
            )}
          </button>
        </form>

        <div className="border-t border-slate-200 px-6 py-4">
          <p className="text-center text-xs leading-relaxed text-slate-500">
            Forgot your password? Contact your clinic administrator to have it reset.
          </p>
        </div>
      </div>
    </main>
  );
}
