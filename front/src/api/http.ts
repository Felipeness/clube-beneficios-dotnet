import type { z } from 'zod';
import type { AccessToken } from './schemas';
import { ProblemDetails } from './schemas';

// EXPO_PUBLIC_* is inlined at bundle time and must be read with this exact static expression. The fallback serves
// the browser on the same PC; a phone needs the PC's LAN IP in .env.local (see README).
export const apiBaseUrl = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5135';

const DEFAULT_TIMEOUT_MS = 10_000;

export type ApiFailure =
  | { readonly kind: 'problem'; readonly status: number; readonly code: string | null; readonly title: string | null }
  | { readonly kind: 'timeout'; readonly timeoutMs: number }
  | { readonly kind: 'network' }
  | { readonly kind: 'invalid-response'; readonly status: number };

export class ApiError extends Error {
  constructor(
    readonly failure: ApiFailure,
    readonly operation: string,
    options?: ErrorOptions,
  ) {
    super(`${operation}: ${failure.kind}`, options);
    this.name = 'ApiError';
  }
}

export type CallContext = { readonly token?: AccessToken | null; readonly signal?: AbortSignal };

type ApiRequest<S extends z.ZodType> = CallContext & {
  readonly method: 'GET' | 'POST';
  readonly path: string;
  readonly schema: S;
  readonly body?: unknown;
  readonly headers?: Readonly<Record<string, string>>;
  readonly timeoutMs?: number;
  // Statuses whose body is the answer, not an error (the health probe answers 503 with the same JSON shape).
  readonly answerStatuses?: readonly number[];
};

export async function apiRequest<S extends z.ZodType>(request: ApiRequest<S>): Promise<z.output<S>> {
  const operation = `${request.method} ${request.path}`;
  const response = await send(request, operation);
  const isAnswer = response.ok || (request.answerStatuses ?? []).includes(response.status);
  if (!isAnswer) throw new ApiError(await problemOf(response), operation);

  const parsed = request.schema.safeParse(await jsonOf(response));
  if (!parsed.success) {
    throw new ApiError({ kind: 'invalid-response', status: response.status }, operation, { cause: parsed.error });
  }
  return parsed.data;
}

// One AbortController carries both the caller's cancellation (TanStack Query unmounting) and the timeout, because
// AbortSignal.timeout/any are not guaranteed on Hermes.
async function send(request: ApiRequest<z.ZodType>, operation: string): Promise<Response> {
  const timeoutMs = request.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const controller = new AbortController();
  let hasTimedOut = false;
  const timer = setTimeout(() => {
    hasTimedOut = true;
    controller.abort();
  }, timeoutMs);
  const forwardCancel = () => controller.abort();
  request.signal?.addEventListener('abort', forwardCancel, { once: true });

  try {
    return await fetch(`${apiBaseUrl}${request.path}`, {
      method: request.method,
      headers: headersOf(request),
      body: request.body === undefined ? undefined : JSON.stringify(request.body),
      signal: controller.signal,
    });
  } catch (cause) {
    if (hasTimedOut) throw new ApiError({ kind: 'timeout', timeoutMs }, operation, { cause });
    if (request.signal?.aborted) throw cause;
    throw new ApiError({ kind: 'network' }, operation, { cause });
  } finally {
    clearTimeout(timer);
    request.signal?.removeEventListener('abort', forwardCancel);
  }
}

function headersOf(request: ApiRequest<z.ZodType>): Record<string, string> {
  return {
    Accept: 'application/json',
    ...(request.body === undefined ? {} : { 'Content-Type': 'application/json' }),
    ...(request.token ? { Authorization: `Bearer ${request.token}` } : {}),
    ...request.headers,
  };
}

async function problemOf(response: Response): Promise<ApiFailure> {
  const problem = ProblemDetails.safeParse(await jsonOf(response));
  return {
    kind: 'problem',
    status: response.status,
    code: problem.success ? (problem.data.code ?? null) : null,
    title: problem.success ? (problem.data.title ?? null) : null,
  };
}

// An empty or non-JSON body (a bare 401, a proxy's HTML page) becomes undefined and fails the schema, never a crash.
async function jsonOf(response: Response): Promise<unknown> {
  const text = await response.text();
  if (text.length === 0) return undefined;
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}
