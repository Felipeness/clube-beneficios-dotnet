import { useMutation, useQueryClient } from '@tanstack/react-query';
import { randomUUID } from 'expo-crypto';
import { useState } from 'react';
import { type RedemptionRequest, redeemReward } from '@/api/clube-api';
import { describeError, errorCodeOf, type HumanError } from '@/api/error-messages';
import { queryKeys } from '@/api/query-client';
import type { Cents } from '@/money/money';
import type { MemberSession } from '@/session/session';
import { confirmWithHaptics } from '@/ui/haptics';

type DeliveryOutcome =
  | { readonly outcome: 'redeemed'; readonly balance: Cents }
  | { readonly outcome: 'refused'; readonly error: HumanError; readonly code: string | null };

export type Delivery = DeliveryOutcome & { readonly sequence: number };

// One redemption attempt = one Idempotency-Key. Every resend of the attempt reuses the key, which is what lets the
// server recognize a retry and answer with the original result instead of charging again.
export type RedemptionAttempt = {
  readonly request: RedemptionRequest;
  readonly deliveries: readonly Delivery[];
};

export function useRedemption(session: MemberSession) {
  const queryClient = useQueryClient();
  const [attempt, setAttempt] = useState<RedemptionAttempt | null>(null);
  const redeem = useMutation({
    mutationFn: (request: RedemptionRequest) => redeemReward(request, { token: session.token }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.membership(session.membershipId) }),
  });

  const deliver = (request: RedemptionRequest) => {
    const record = (delivery: DeliveryOutcome) =>
      setAttempt((current) =>
        current?.request.idempotencyKey === request.idempotencyKey
          ? {
              ...current,
              deliveries: [...current.deliveries, { ...delivery, sequence: current.deliveries.length + 1 }],
            }
          : current,
      );
    redeem.mutate(request, {
      onSuccess: (membership) => {
        confirmWithHaptics('success');
        record({ outcome: 'redeemed', balance: membership.balanceCents });
      },
      onError: (error) => {
        confirmWithHaptics('error');
        record({ outcome: 'refused', error: describeError(error), code: errorCodeOf(error) });
      },
    });
  };

  const start = (rewardId: string) => {
    // expo-crypto draws from the platform CSPRNG; Math.random would make keys guessable and collision-prone.
    const request = { membershipId: session.membershipId, rewardId, idempotencyKey: randomUUID() };
    setAttempt({ request, deliveries: [] });
    deliver(request);
  };

  const repeat = () => {
    if (attempt) deliver(attempt.request);
  };

  return { attempt, start, repeat, isSending: redeem.isPending, sendingRewardId: redeem.variables?.rewardId };
}
