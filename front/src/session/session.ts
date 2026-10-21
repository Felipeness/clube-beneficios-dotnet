import { z } from 'zod';
import { AccessToken, MembershipId } from '@/api/schemas';

const expiresAt = z.iso.datetime({ offset: true }).transform((value) => new Date(value));

// What survives an app restart. Parsed on the way back in, like any other untrusted input: a stale or tampered value
// in storage becomes "signed out", never a half-built session.
export const Session = z.discriminatedUnion('role', [
  z.object({ role: z.literal('member'), token: AccessToken, expiresAt, membershipId: MembershipId }),
  z.object({ role: z.literal('operator'), token: AccessToken, expiresAt, operatorName: z.string().min(1) }),
]);
export type Session = z.infer<typeof Session>;
export type MemberSession = Extract<Session, { role: 'member' }>;
export type OperatorSession = Extract<Session, { role: 'operator' }>;

export type SessionState =
  | { readonly status: 'loading' }
  | { readonly status: 'signedOut' }
  | { readonly status: 'signedIn'; readonly session: Session };

export function serializeSession(session: Session): string {
  return JSON.stringify({ ...session, expiresAt: session.expiresAt.toISOString() });
}

export function restoreSession(stored: string | null, now: Date): Session | null {
  if (stored === null) return null;
  const parsed = Session.safeParse(parseJson(stored));
  if (!parsed.success) return null;
  return parsed.data.expiresAt > now ? parsed.data : null;
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}
