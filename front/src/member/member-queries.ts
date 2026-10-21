import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { getDevClock, getMembership, getStatementPage } from '@/api/clube-api';
import { queryKeys } from '@/api/query-client';
import type { AccessToken, MembershipId } from '@/api/schemas';

// Shared by the member's own screens and the operator's lookup: same cache entries, so a reversal done at the counter
// shows up on the member's statement without anything else to wire.

export function useMembershipQuery(membershipId: MembershipId, token: AccessToken) {
  return useQuery({
    queryKey: queryKeys.membership(membershipId),
    queryFn: ({ signal }) => getMembership(membershipId, { token, signal }),
  });
}

const firstStatementPage = (): string | null => null;

export function useStatementQuery(membershipId: MembershipId, token: AccessToken) {
  return useInfiniteQuery({
    queryKey: queryKeys.statement(membershipId),
    queryFn: ({ pageParam, signal }) => getStatementPage(membershipId, pageParam, { token, signal }),
    initialPageParam: firstStatementPage(),
    getNextPageParam: (lastPage) => lastPage.nextCursor,
  });
}

// "Now" is the backend's dev clock, which can be moved forward to demo the billing window and credit expiry.
export function useDevClockQuery() {
  return useQuery({ queryKey: queryKeys.devClock, queryFn: ({ signal }) => getDevClock({ signal }) });
}
