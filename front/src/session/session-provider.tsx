import { useQueryClient } from '@tanstack/react-query';
import { createContext, type ReactNode, use, useEffect, useState } from 'react';
import { isUnauthorized } from '@/api/query-client';
import {
  type MemberSession,
  type OperatorSession,
  restoreSession,
  type Session,
  type SessionState,
  serializeSession,
} from './session';
import { clearStoredSession, readStoredSession, writeStoredSession } from './session-storage';

type SessionValue = {
  readonly state: SessionState;
  readonly signIn: (session: Session) => Promise<void>;
  readonly signOut: () => Promise<void>;
};

const SessionContext = createContext<SessionValue | null>(null);

export function SessionProvider({ children }: { readonly children: ReactNode }) {
  const queryClient = useQueryClient();
  const [state, setState] = useState<SessionState>({ status: 'loading' });

  const signIn = async (session: Session) => {
    await writeStoredSession(serializeSession(session));
    setState({ status: 'signedIn', session });
  };

  // Clearing the cache matters as much as the token: the next person on this device must not see the last one's balance.
  const signOut = async () => {
    await clearStoredSession();
    queryClient.clear();
    setState({ status: 'signedOut' });
  };

  useEffect(() => {
    let isCurrent = true;
    readStoredSession()
      .then((stored) => restoreSession(stored, new Date()))
      .catch((error: unknown) => {
        console.warn('session: stored session unreadable, starting signed out', error);
        return null;
      })
      .then((session) => {
        if (isCurrent) setState(session ? { status: 'signedIn', session } : { status: 'signedOut' });
      });
    return () => {
      isCurrent = false;
    };
  }, []);

  useSignOutOnUnauthorized(signOut);

  return <SessionContext value={{ state, signIn, signOut }}>{children}</SessionContext>;
}

// An expired or revoked token surfaces as a 401 on whatever query runs next; that, not a timer, ends the session.
function useSignOutOnUnauthorized(signOut: () => Promise<void>) {
  const queryClient = useQueryClient();
  useEffect(() => {
    const onError = (error: unknown) => {
      if (isUnauthorized(error)) void signOut();
    };
    const stopQueries = queryClient.getQueryCache().subscribe((event) => {
      if (event.type === 'updated' && event.action.type === 'error') onError(event.action.error);
    });
    const stopMutations = queryClient.getMutationCache().subscribe((event) => {
      if (event.type === 'updated' && event.action.type === 'error') onError(event.action.error);
    });
    return () => {
      stopQueries();
      stopMutations();
    };
  }, [queryClient, signOut]);
}

export function useSession(): SessionValue {
  const value = use(SessionContext);
  if (value === null) throw new Error('useSession: rendered outside SessionProvider');
  return value;
}

// Screens under /member and /operator only render behind Stack.Protected, so a missing session here is a routing bug.
export function useMemberSession(): MemberSession {
  const { state } = useSession();
  if (state.status !== 'signedIn' || state.session.role !== 'member')
    throw new Error('useMemberSession: no member session');
  return state.session;
}

export function useOperatorSession(): OperatorSession {
  const { state } = useSession();
  if (state.status !== 'signedIn' || state.session.role !== 'operator') {
    throw new Error('useOperatorSession: no operator session');
  }
  return state.session;
}
