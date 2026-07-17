// Typed fetch wrapper shared by every feature module. Tests mock `fetch`
// (or the feature modules that call these helpers) so no server is required.

const DEFAULT_BASE_URL = 'http://localhost:5080';

export function apiBaseUrl(): string {
  return import.meta.env.VITE_API_BASE_URL ?? DEFAULT_BASE_URL;
}

// Bearer token + expiry handling. The auth context sets these; every request
// attaches the token, and a 401 notifies the handler so the app can log out.
let authToken: string | null = null;
let unauthorizedHandler: (() => void) | null = null;

export function setAuthToken(token: string | null): void {
  authToken = token;
}

export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler;
}

/** RFC 7807 ProblemDetails body returned by the API on error. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** Thrown for any non-2xx response; carries the parsed ProblemDetails when present. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ProblemDetails;

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, signal } = options;
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (authToken) headers['Authorization'] = `Bearer ${authToken}`;

  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl()}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
    });
  } catch (cause) {
    throw new ApiError(0, `Network error calling ${method} ${path}`, undefined);
  }

  if (!response.ok) {
    if (response.status === 401) unauthorizedHandler?.();
    const problem = await safeParseProblem(response);
    const message = problem?.title ?? problem?.detail ?? `Request failed with status ${response.status}`;
    throw new ApiError(response.status, message, problem);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

async function safeParseProblem(response: Response): Promise<ProblemDetails | undefined> {
  try {
    const data = (await response.json()) as ProblemDetails;
    return data;
  } catch {
    return undefined;
  }
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: 'GET', signal }),
  post: <T>(path: string, body: unknown, signal?: AbortSignal) =>
    request<T>(path, { method: 'POST', body, signal }),
  put: <T>(path: string, body: unknown, signal?: AbortSignal) =>
    request<T>(path, { method: 'PUT', body, signal }),
  patch: <T>(path: string, body: unknown, signal?: AbortSignal) =>
    request<T>(path, { method: 'PATCH', body, signal }),
  del: <T>(path: string, signal?: AbortSignal) => request<T>(path, { method: 'DELETE', signal }),
};
