import { useEffect, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { login } from '../api/auth';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { roleRoutes } from '../auth/roleRoutes';
import { Banner } from '../components/ui/Banner';
import { Button } from '../components/ui/Button';
import { Field, RequiredLegend } from '../components/ui/Field';
import swiftcareLogo from '../assets/swiftcare-logo.png';

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

  useEffect(() => {
    document.title = 'Staff Sign-In · SwiftCare';
  }, []);

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
        setServerMessage('Invalid username or password.');
      } else if (error instanceof ApiError && error.status === 403) {
        setStatus('deactivated');
        setServerMessage('Your account has been deactivated. Contact your administrator.');
      } else if (error instanceof ApiError && error.status === 429) {
        setStatus('rejected');
        setServerMessage('Too many attempts. Please wait a moment and try again.');
      } else {
        setStatus('rejected');
        setServerMessage(GENERIC_ERROR_MESSAGE);
      }
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-slate-50 px-4 py-10">
      <div className="w-full max-w-md rounded-xl border border-slate-200 bg-white shadow-[0_1px_2px_rgb(15_23_42/0.04),0_8px_24px_rgb(15_23_42/0.06)]">
        <div className="px-6 pt-8 text-center sm:px-8">
          <img src={swiftcareLogo} alt="SwiftCare" width={603} height={176} className="mx-auto h-14 w-auto" />
          <h1 className="mt-6 text-xl font-semibold tracking-tight text-slate-900">Staff Sign-In</h1>
          <p className="mt-1 text-sm text-slate-500">Use your clinic account to continue.</p>
        </div>

        {/* Status region - one persistent aria-live container, content swapped by status */}
        <div aria-live="polite" className="px-6 pt-5 empty:hidden sm:px-8">
          {status === 'rejected' && (
            <Banner tone="error" title="Access Denied">
              {serverMessage}
            </Banner>
          )}
          {status === 'deactivated' && (
            <Banner tone="warning" title="Account Deactivated">
              {serverMessage}
            </Banner>
          )}
          {status === 'issued' && (
            <Banner tone="success" title="Access Granted">
              Redirecting to your dashboard…
            </Banner>
          )}
        </div>

        <form onSubmit={handleSubmit} noValidate className="space-y-5 px-6 py-6 sm:px-8">
          <RequiredLegend />

          <Field id="username" label="Username" required error={fieldErrors.username}>
            {(control) => (
              <input
                {...control}
                name="username"
                type="text"
                autoComplete="username"
                autoFocus
                value={username}
                onChange={(event) => handleUsernameChange(event.target.value)}
                disabled={isBusy}
              />
            )}
          </Field>

          <Field id="password" label="Password" required error={fieldErrors.password}>
            {(control) => (
              <input
                {...control}
                name="password"
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(event) => handlePasswordChange(event.target.value)}
                disabled={isBusy}
              />
            )}
          </Field>

          <Button type="submit" fullWidth loading={isBusy}>
            {status === 'submitting' ? 'Verifying…' : status === 'issued' ? 'Access Granted' : 'Sign In'}
          </Button>
        </form>

        <div className="border-t border-slate-100 px-6 py-4 sm:px-8">
          <p className="text-center text-xs leading-relaxed text-slate-500">
            Forgot your password? Contact your clinic administrator to have it reset.
          </p>
        </div>
      </div>
    </main>
  );
}
