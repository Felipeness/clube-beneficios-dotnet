import { QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react-native';
import * as SecureStore from 'expo-secure-store';
import type { ReactNode } from 'react';
import { createQueryClient } from '@/api/query-client';
import { AccessToken, MembershipId } from '@/api/schemas';
import { type Session, serializeSession } from '@/session/session';
import { SessionProvider, useSession } from '@/session/session-provider';

const IN_EIGHT_HOURS = () => new Date(Date.now() + 8 * 60 * 60 * 1000);

export const memberSession = (membershipId: string): Session => ({
  role: 'member',
  token: AccessToken.parse('member-token'),
  expiresAt: IN_EIGHT_HOURS(),
  membershipId: MembershipId.parse(membershipId),
});

export const operatorSession = (operatorName: string): Session => ({
  role: 'operator',
  token: AccessToken.parse('operator-token'),
  expiresAt: IN_EIGHT_HOURS(),
  operatorName,
});

// The same key the app uses: tests go through the real SessionProvider and its storage, not a stubbed context.
export const storeSession = (session: Session) => SecureStore.setItemAsync('clube.session', serializeSession(session));

function SignedInOnly({ children }: { readonly children: ReactNode }) {
  const { state } = useSession();
  return state.status === 'signedIn' ? children : null;
}

export async function renderSignedIn(ui: ReactNode, session: Session) {
  await storeSession(session);
  const queryClient = createQueryClient();
  return {
    queryClient,
    ...(await render(
      <QueryClientProvider client={queryClient}>
        <SessionProvider>
          <SignedInOnly>{ui}</SignedInOnly>
        </SessionProvider>
      </QueryClientProvider>,
    )),
  };
}
