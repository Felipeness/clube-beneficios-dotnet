import { z } from 'zod';
import { Cents } from '@/money/money';

// Every response is parsed here before the app touches it. Ids are branded so a transaction id cannot be passed
// where a membership id is expected, and dates arrive as Date, not as strings to be re-parsed in a screen.

const timestamp = z.iso.datetime({ offset: true }).transform((value) => new Date(value));

export const MembershipId = z.guid().brand<'MembershipId'>();
export type MembershipId = z.infer<typeof MembershipId>;

export const TransactionId = z.guid().brand<'TransactionId'>();
export type TransactionId = z.infer<typeof TransactionId>;

export const AccessToken = z.string().min(1).brand<'AccessToken'>();
export type AccessToken = z.infer<typeof AccessToken>;

export const IssuedToken = z.object({ accessToken: AccessToken, expiresAt: timestamp });
export type IssuedToken = z.infer<typeof IssuedToken>;

export const Membership = z.object({
  id: MembershipId,
  holder: z.string(),
  balanceCents: Cents,
  subscribedAt: timestamp,
});
export type Membership = z.infer<typeof Membership>;

export const ActorKind = z.enum(['Anonymous', 'Member', 'PaymentProvider', 'Operator', 'System']);
export type ActorKind = z.infer<typeof ActorKind>;

export const StatementLine = z.object({
  transactionId: TransactionId,
  description: z.string(),
  amountCents: Cents,
  postedAt: timestamp,
  actorKind: ActorKind,
  actorId: z.string().nullable(),
});
export type StatementLine = z.infer<typeof StatementLine>;

export const StatementPage = z.object({
  membershipId: MembershipId,
  balanceCents: Cents,
  lines: z.array(StatementLine),
  nextCursor: z.string().nullable(),
});
export type StatementPage = z.infer<typeof StatementPage>;

export const Reward = z.object({
  id: z.string().min(1),
  name: z.string(),
  listPriceCents: Cents,
  discountPercent: z.number().nonnegative(),
  priceCents: Cents,
});
export type Reward = z.infer<typeof Reward>;

export const PixPaymentResult = z.enum(['credited', 'already_credited', 'duplicate_payment']);
export type PixPaymentResult = z.infer<typeof PixPaymentResult>;

export const SimulatedPixPayment = z.object({ endToEndId: z.string().min(1), result: PixPaymentResult });
export type SimulatedPixPayment = z.infer<typeof SimulatedPixPayment>;

export const DevClock = z.object({ now: timestamp, offsetDays: z.number().int().nonnegative() });
export type DevClock = z.infer<typeof DevClock>;

export const OutboxRun = z.object({
  attempted: z.number().int(),
  delivered: z.number().int(),
  rescheduled: z.number().int(),
  deadLettered: z.number().int(),
});
export type OutboxRun = z.infer<typeof OutboxRun>;

export const CreditExpiryRun = z.object({
  cutoff: z.string(),
  expired: z.number().int(),
  expiredCents: Cents,
  failures: z.array(z.object({ membershipId: MembershipId, error: z.string() })),
});
export type CreditExpiryRun = z.infer<typeof CreditExpiryRun>;

export const DivergenceKind = z.enum(['MissingInLedger', 'MissingAtPsp', 'AmountMismatch']);
export type DivergenceKind = z.infer<typeof DivergenceKind>;

export const Reconciliation = z.object({
  day: z.iso.date(),
  settledAtPsp: z.number().int(),
  creditedInLedger: z.number().int(),
  divergences: z.array(
    z.object({
      kind: DivergenceKind,
      endToEndId: z.string(),
      pspCents: Cents.nullable(),
      ledgerCents: Cents.nullable(),
    }),
  ),
});
export type Reconciliation = z.infer<typeof Reconciliation>;

export const LedgerHealth = z.object({
  status: z.enum(['Healthy', 'Degraded', 'Unhealthy']),
  differenceCents: Cents.nullable(),
});
export type LedgerHealth = z.infer<typeof LedgerHealth>;

// RFC 9457 body plus the "code" extension the API adds to every business error.
export const ProblemDetails = z.object({
  title: z.string().optional(),
  detail: z.string().optional(),
  code: z.string().optional(),
});
