// Web build. SecureStore does not exist in the browser, so the session goes to localStorage, which any script on the
// page can read: an XSS would leak the token. Accepted here because tokens are short-lived dev tokens and surviving a
// refresh matters for a demo. In production the API should set an HttpOnly cookie instead (CORS with credentials and
// an explicit origin), and the app would keep no token at all.
const KEY = 'clube.session';

export async function readStoredSession(): Promise<string | null> {
  return globalThis.localStorage?.getItem(KEY) ?? null;
}

export async function writeStoredSession(value: string): Promise<void> {
  globalThis.localStorage?.setItem(KEY, value);
}

export async function clearStoredSession(): Promise<void> {
  globalThis.localStorage?.removeItem(KEY);
}
