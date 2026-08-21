import { getStoredToken } from '../auth/tokenStorage';

const GATEWAY_URL = import.meta.env.VITE_GATEWAY_URL as string | undefined;

if (!GATEWAY_URL) {
  throw new Error('VITE_GATEWAY_URL is not configured. Set it in frontend/.env.');
}

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
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
    try {
      const body = (await response.json()) as { message?: string };
      message = body.message ?? message;
    } catch {
      // No JSON body on this error response - fall back to the generic message.
    }
    throw new ApiError(response.status, message);
  }

  if (response.status === 204) {
    return undefined as TResponse;
  }

  return (await response.json()) as TResponse;
}
