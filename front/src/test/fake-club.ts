import { type HttpHandler, HttpResponse, http } from 'msw';
import { apiBaseUrl } from '@/api/http';

// An in-memory stand-in for the API, just big enough for the screens under test. It keeps the rules the app relies on
// (balance checks, one charge per Idempotency-Key) so tests exercise behavior, not canned responses.

export const MEMBERSHIP_ID = '0199f0a4-7c1e-7000-8000-000000000001';

const rewards = [
  { id: 'cinema-ticket', name: 'Cinema ticket', listPriceCents: 1000, discountPercent: 0, priceCents: 1000 },
  { id: 'thermal-bottle', name: 'Thermal bottle', listPriceCents: 2990, discountPercent: 15, priceCents: 2541 },
];

type FakeClub = {
  readonly handlers: HttpHandler[];
  readonly redemptionKeys: string[];
  readonly membershipReads: () => number;
  readonly balance: () => number;
};

export function fakeClub(initialBalanceCents: number): FakeClub {
  let balanceCents = initialBalanceCents;
  let reads = 0;
  const redemptionKeys: string[] = [];
  const answered = new Map<string, number>();

  const membership = () => ({
    id: MEMBERSHIP_ID,
    holder: '***.982.247-**',
    balanceCents,
    subscribedAt: '2026-10-01T12:00:00+00:00',
  });

  const problem = (status: number, code: string) =>
    HttpResponse.json(
      { title: code, status, code },
      { status, headers: { 'Content-Type': 'application/problem+json' } },
    );

  const handlers = [
    http.get(`${apiBaseUrl}/rewards`, () => HttpResponse.json(rewards)),
    http.get(`${apiBaseUrl}/memberships/:id`, () => {
      reads += 1;
      return HttpResponse.json(membership());
    }),
    http.post(`${apiBaseUrl}/memberships/:id/redemptions`, async ({ request }) => {
      const key = request.headers.get('Idempotency-Key') ?? '';
      redemptionKeys.push(key);
      const replay = answered.get(key);
      if (replay !== undefined) return HttpResponse.json({ ...membership(), balanceCents: replay });

      const body: unknown = await request.json();
      const rewardId = typeof body === 'object' && body !== null && 'rewardId' in body ? body.rewardId : null;
      const reward = rewards.find((candidate) => candidate.id === rewardId);
      if (!reward) return problem(404, 'membership.reward_not_found');
      if (balanceCents < reward.priceCents) return problem(422, 'membership.insufficient_balance');

      balanceCents -= reward.priceCents;
      answered.set(key, balanceCents);
      return HttpResponse.json(membership());
    }),
  ];

  return { handlers, redemptionKeys, membershipReads: () => reads, balance: () => balanceCents };
}
