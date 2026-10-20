import { z } from 'zod';
import { apiRequest, type CallContext } from './http';
import {
  CreditExpiryRun,
  DevClock,
  IssuedToken,
  LedgerHealth,
  Membership,
  type MembershipId,
  OutboxRun,
  Reconciliation,
  Reward,
  SimulatedPixPayment,
  StatementPage,
  type TransactionId,
} from './schemas';

// The whole HTTP surface the app uses, one typed function per route. Screens never build a URL or parse a body.

export type TokenRequest =
  | { readonly role: 'member'; readonly membershipId: MembershipId }
  | { readonly role: 'operator'; readonly name: string };

export const issueDevToken = (request: TokenRequest, context: CallContext = {}) =>
  apiRequest({ ...context, method: 'POST', path: '/dev/tokens', body: request, schema: IssuedToken });

export const subscribe = (cpf: string, context: CallContext = {}) =>
  apiRequest({ ...context, method: 'POST', path: '/memberships', body: { cpf }, schema: Membership });

export const getMembership = (id: MembershipId, context: CallContext) =>
  apiRequest({ ...context, method: 'GET', path: `/memberships/${id}`, schema: Membership });

export const STATEMENT_PAGE_SIZE = 20;

export const getStatementPage = (id: MembershipId, cursor: string | null, context: CallContext) => {
  const query = new URLSearchParams({ limit: String(STATEMENT_PAGE_SIZE), ...(cursor ? { cursor } : {}) });
  return apiRequest({
    ...context,
    method: 'GET',
    path: `/memberships/${id}/statement?${query}`,
    schema: StatementPage,
  });
};

export const listRewards = (context: CallContext = {}) =>
  apiRequest({ ...context, method: 'GET', path: '/rewards', schema: z.array(Reward) });

export type RedemptionRequest = {
  readonly membershipId: MembershipId;
  readonly rewardId: string;
  readonly idempotencyKey: string;
};

export const redeemReward = (request: RedemptionRequest, context: CallContext) =>
  apiRequest({
    ...context,
    method: 'POST',
    path: `/memberships/${request.membershipId}/redemptions`,
    headers: { 'Idempotency-Key': request.idempotencyKey },
    body: { rewardId: request.rewardId },
    schema: Membership,
  });

export const reverseTransaction = (membershipId: MembershipId, transactionId: TransactionId, context: CallContext) =>
  apiRequest({
    ...context,
    method: 'POST',
    path: `/memberships/${membershipId}/transactions/${transactionId}/reversal`,
    schema: Membership,
  });

export const payPix = (membershipId: MembershipId, period: string, context: CallContext = {}) =>
  apiRequest({
    ...context,
    method: 'POST',
    path: '/dev/pix-payments',
    body: { membershipId, period },
    schema: SimulatedPixPayment,
  });

export const getLedgerHealth = (context: CallContext = {}) =>
  apiRequest({ ...context, method: 'GET', path: '/health/ledger', schema: LedgerHealth, answerStatuses: [503] });

export const getReconciliation = (date: string, context: CallContext) =>
  apiRequest({ ...context, method: 'GET', path: `/operations/reconciliation?date=${date}`, schema: Reconciliation });

export const getDevClock = (context: CallContext = {}) =>
  apiRequest({ ...context, method: 'GET', path: '/dev/clock', schema: DevClock });

export type ClockChange = { readonly advanceDays: number } | { readonly reset: true };

export const changeDevClock = (change: ClockChange, context: CallContext = {}) =>
  apiRequest({ ...context, method: 'POST', path: '/dev/clock', body: change, schema: DevClock });

// Jobs process the whole backlog in one call, so they get more time than an interactive read.
const JOB_TIMEOUT_MS = 30_000;

export const runOutboxJob = (context: CallContext = {}) =>
  apiRequest({ ...context, method: 'POST', path: '/dev/jobs/outbox', schema: OutboxRun, timeoutMs: JOB_TIMEOUT_MS });

export const runCreditExpiryJob = (context: CallContext = {}) =>
  apiRequest({
    ...context,
    method: 'POST',
    path: '/dev/jobs/credit-expiry',
    schema: CreditExpiryRun,
    timeoutMs: JOB_TIMEOUT_MS,
  });

export const runReconciliationJob = (date: string, context: CallContext = {}) =>
  apiRequest({
    ...context,
    method: 'POST',
    path: '/dev/jobs/reconciliation',
    body: { date },
    schema: Reconciliation,
    timeoutMs: JOB_TIMEOUT_MS,
  });
