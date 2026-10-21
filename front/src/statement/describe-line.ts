import type { ActorKind, StatementLine } from '@/api/schemas';
import { formatPeriod, formatPeriodShort } from '@/member/billing-period';
import { presentReward } from '@/rewards/reward-catalog';

export type Perspective = 'member' | 'operator';

// The ledger writes descriptions in English for its own audit trail; the statement translates the known shapes and
// shows anything new verbatim rather than hiding it.
const describers: ReadonlyArray<readonly [RegExp, (match: RegExpExecArray) => string]> = [
  [/^Monthly allowance (\d{4}-\d{2})$/, (match) => `Mensalidade de ${formatPeriod(match[1] ?? '')}`],
  [/^Redemption (.+)$/, (match) => `Resgate: ${presentReward(match[1] ?? '', match[1] ?? '').name}`],
  [/^Reversal of (.+)$/, (match) => `Estorno: ${describeDescription(match[1] ?? '')}`],
  [
    /^Credit expired \(credits up to (\d{4}-\d{2})\)$/,
    (match) => `Créditos expirados (até ${formatPeriodShort(match[1] ?? '')})`,
  ],
];

export function describeDescription(description: string): string {
  for (const [pattern, describe] of describers) {
    const match = pattern.exec(description);
    if (match) return describe(match);
  }
  return description;
}

const actorLabels: Record<ActorKind, (actorId: string | null, perspective: Perspective) => string> = {
  Member: (_, perspective) => (perspective === 'member' ? 'Você' : 'Membro'),
  PaymentProvider: () => 'PSP Pix',
  Operator: (actorId) => (actorId ? `Operador ${actorId}` : 'Operador'),
  System: (actorId) => (actorId === 'credit-expiry' ? 'Sistema (expiração)' : 'Sistema'),
  Anonymous: () => 'Anônimo',
};

export function describeActor(line: Pick<StatementLine, 'actorKind' | 'actorId'>, perspective: Perspective): string {
  return actorLabels[line.actorKind](line.actorId, perspective);
}

export const isReversal = (line: Pick<StatementLine, 'description'>): boolean =>
  line.description.startsWith('Reversal of ');

const postedAtFormat = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

// Device time zone on purpose: the statement answers "when did this happen for me".
export const formatPostedAt = (postedAt: Date): string => postedAtFormat.format(postedAt);
