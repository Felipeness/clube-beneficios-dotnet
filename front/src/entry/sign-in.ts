import { useMutation } from '@tanstack/react-query';
import { getMembership, issueDevToken } from '@/api/clube-api';
import type { MembershipId } from '@/api/schemas';
import { useSession } from '@/session/session-provider';

// Tokens come from the backend's dev issuer (POST /dev/tokens): the app never signs anything itself.

export function useMemberSignIn() {
  const { signIn } = useSession();
  return useMutation({
    mutationFn: async (membershipId: MembershipId) => {
      const issued = await issueDevToken({ role: 'member', membershipId });
      // The issuer signs any id; reading the membership first turns a typo into "not found" here, not on every tab.
      await getMembership(membershipId, { token: issued.accessToken });
      await signIn({ role: 'member', token: issued.accessToken, expiresAt: issued.expiresAt, membershipId });
    },
  });
}

export function useOperatorSignIn() {
  const { signIn } = useSession();
  return useMutation({
    mutationFn: async (operatorName: string) => {
      const issued = await issueDevToken({ role: 'operator', name: operatorName });
      await signIn({ role: 'operator', token: issued.accessToken, expiresAt: issued.expiresAt, operatorName });
    },
  });
}
