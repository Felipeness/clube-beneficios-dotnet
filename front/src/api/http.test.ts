import { delay, HttpResponse, http } from 'msw';
import { z } from 'zod';
import { server } from '@/test/server';
import { ApiError, type ApiFailure, apiBaseUrl, apiRequest } from './http';
import { AccessToken, Membership } from './schemas';

const membershipJson = {
  id: '0199f0a4-7c1e-7000-8000-000000000001',
  holder: '***.982.247-**',
  balanceCents: 2000,
  subscribedAt: '2026-10-01T12:00:00.1234567+00:00',
};

async function failureOf(promise: Promise<unknown>): Promise<ApiFailure> {
  try {
    await promise;
  } catch (error) {
    if (error instanceof ApiError) return error.failure;
    throw error;
  }
  throw new Error('expected the request to fail');
}

describe('apiRequest', () => {
  it('parses a successful body into branded values and dates', async () => {
    server.use(http.get(`${apiBaseUrl}/memberships/:id`, () => HttpResponse.json(membershipJson)));

    const membership = await apiRequest({
      method: 'GET',
      path: `/memberships/${membershipJson.id}`,
      schema: Membership,
    });

    expect(membership.balanceCents).toBe(2000);
    expect(membership.subscribedAt).toEqual(new Date('2026-10-01T12:00:00.123Z'));
  });

  it('sends the bearer token and the extra headers', async () => {
    let seen: Headers | undefined;
    server.use(
      http.post(`${apiBaseUrl}/echo`, ({ request }) => {
        seen = request.headers;
        return HttpResponse.json({});
      }),
    );

    await apiRequest({
      method: 'POST',
      path: '/echo',
      body: { rewardId: 'cinema-ticket' },
      headers: { 'Idempotency-Key': 'key-1' },
      token: AccessToken.parse('jwt-value'),
      schema: z.object({}),
    });

    expect(seen?.get('authorization')).toBe('Bearer jwt-value');
    expect(seen?.get('idempotency-key')).toBe('key-1');
    expect(seen?.get('content-type')).toBe('application/json');
  });

  it('turns a ProblemDetails body into a typed problem with its code', async () => {
    server.use(
      http.post(`${apiBaseUrl}/memberships`, () =>
        HttpResponse.json(
          { title: 'Balance does not cover this reward.', status: 422, code: 'membership.insufficient_balance' },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );

    const failure = await failureOf(apiRequest({ method: 'POST', path: '/memberships', body: {}, schema: Membership }));

    expect(failure).toEqual({
      kind: 'problem',
      status: 422,
      code: 'membership.insufficient_balance',
      title: 'Balance does not cover this reward.',
    });
  });

  it('keeps the status when an error has no body', async () => {
    server.use(http.get(`${apiBaseUrl}/private`, () => new HttpResponse(null, { status: 401 })));

    const failure = await failureOf(apiRequest({ method: 'GET', path: '/private', schema: z.object({}) }));

    expect(failure).toEqual({ kind: 'problem', status: 401, code: null, title: null });
  });

  it('rejects a success body that does not match the schema', async () => {
    server.use(
      http.get(`${apiBaseUrl}/memberships/:id`, () => HttpResponse.json({ ...membershipJson, balanceCents: 10.5 })),
    );

    const failure = await failureOf(
      apiRequest({ method: 'GET', path: `/memberships/${membershipJson.id}`, schema: Membership }),
    );

    expect(failure).toEqual({ kind: 'invalid-response', status: 200 });
  });

  it('gives up after the timeout and says so', async () => {
    server.use(
      http.get(`${apiBaseUrl}/slow`, async () => {
        await delay(500);
        return HttpResponse.json({});
      }),
    );

    const failure = await failureOf(apiRequest({ method: 'GET', path: '/slow', schema: z.object({}), timeoutMs: 50 }));

    expect(failure).toEqual({ kind: 'timeout', timeoutMs: 50 });
  });

  it('reports an unreachable server as a network failure', async () => {
    server.use(http.get(`${apiBaseUrl}/down`, () => HttpResponse.error()));

    const failure = await failureOf(apiRequest({ method: 'GET', path: '/down', schema: z.object({}) }));

    expect(failure).toEqual({ kind: 'network' });
  });

  it('accepts a declared non-2xx status as an answer', async () => {
    server.use(
      http.get(`${apiBaseUrl}/health/ledger`, () =>
        HttpResponse.json({ status: 'Unhealthy', differenceCents: 150 }, { status: 503 }),
      ),
    );

    const health = await apiRequest({
      method: 'GET',
      path: '/health/ledger',
      schema: z.object({ status: z.string(), differenceCents: z.number() }),
      answerStatuses: [503],
    });

    expect(health).toEqual({ status: 'Unhealthy', differenceCents: 150 });
  });
});
