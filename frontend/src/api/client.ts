import { getStoredToken } from '../auth/tokenStorage';

const GATEWAY_URL = import.meta.env.VITE_GATEWAY_URL as string | undefined;

if (!GATEWAY_URL) {
  throw new Error('VITE_GATEWAY_URL is not configured. Set it in frontend/.env.');
}

export class ApiError extends Error {
  readonly status: number;
  // Keyed by lowercased field name. Populated only when the server responded with
  // ASP.NET's ValidationProblemDetails shape ({ errors: { Field: ["message"] } });
  // empty for every other error response, including the plain MessageResponse shape
  // login/logout use.
  readonly fieldErrors: Readonly<Record<string, string>>;

  constructor(status: number, message: string, fieldErrors: Record<string, string> = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
}

interface RequestOptions {
  method?: string;
  body?: unknown;
}

// The single fetch chokepoint for the app: every call gets a correlation ID
// for tracing across the Gateway and services, and the bearer token from
// sessionStorage when one is present. No component should call fetch directly.
export async function apiRequest<TResponse>(path: string, options: RequestOptions = {}): Promise<TResponse> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'X-Correlation-ID': crypto.randomUUID(),
  };

  const token = getStoredToken();
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(`${GATEWAY_URL}${path}`, {
    method: options.method ?? 'GET',
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
  });

  if (!response.ok) {
    let message = 'Request failed';
    let fieldErrors: Record<string, string> = {};
    try {
      const body = (await response.json()) as { message?: string; errors?: Record<string, string[]> };
      message = body.message ?? message;
      if (body.errors) {
        // [ApiController]'s ValidationProblemDetails keys errors by C# property name
        // (e.g. "Username") or, for a body-binding failure, a JSON path (e.g. "$.role").
        // Lowercasing lets the form look field errors up without caring which shape the
        // server produced, and only the first message per field is kept for display.
        fieldErrors = Object.fromEntries(
          Object.entries(body.errors)
            .filter(([, messages]) => messages.length > 0)
            .map(([field, messages]) => [field.replace(/^\$\.?/, '').toLowerCase(), messages[0]]),
        );
      }
    } catch {
      // No JSON body on this error response - fall back to the generic message.
    }
    throw new ApiError(response.status, message, fieldErrors);
  }

  if (response.status === 204) {
    return undefined as TResponse;
  }

  return (await response.json()) as TResponse;
}
