import { QueryClient } from '@tanstack/react-query';
import { ApiError } from './http';
import type { MembershipId } from './schemas';

// Keys are named by the query they cache. Everything about one membership hangs under ['membership', id], so a single
// invalidation after a redemption, a Pix or a reversal refreshes balance and statement together.
export const queryKeys = {
  membership: (id: MembershipId) => ['membership', id] as const,
  statement: (id: MembershipId) => ['membership', id, 'statement'] as const,
  allMemberships: ['membership'] as const,
  rewards: ['rewards'] as const,
  devClock: ['dev', 'clock'] as const,
  ledgerHealth: ['health', 'ledger'] as const,
  reconciliation: (date: string) => ['operations', 'reconciliation', date] as const,
};

const MAX_RETRIES = 2;

// A 4xx is the server's final word (bad input, missing permission, business rule): repeating it changes nothing.
// Only what can heal by itself is retried: timeouts, dropped connections and 5xx.
export function isTransient(error: unknown): boolean {
  if (!(error instanceof ApiError)) return false;
  const { failure } = error;
  return (
    failure.kind === 'timeout' || failure.kind === 'network' || (failure.kind === 'problem' && failure.status >= 500)
  );
}

export function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.failure.kind === 'problem' && error.failure.status === 401;
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: (failureCount, error) => failureCount < MAX_RETRIES && isTransient(error),
        staleTime: 15_000,
      },
      // A write is never replayed behind the user's back; redemption retries are explicit and carry the same key.
      mutations: { retry: false },
    },
  });
}
