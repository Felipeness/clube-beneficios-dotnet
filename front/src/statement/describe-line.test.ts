import type { ActorKind } from '@/api/schemas';
import { describeActor, describeDescription, isReversal, type Perspective } from './describe-line';

describe('describeDescription', () => {
  it.each([
    { raw: 'Monthly allowance 2026-10', text: 'Mensalidade de outubro de 2026' },
    { raw: 'Redemption thermal-bottle', text: 'Resgate: Garrafa térmica' },
    { raw: 'Redemption mystery-box', text: 'Resgate: mystery-box' },
    { raw: 'Reversal of Redemption cinema-ticket', text: 'Estorno: Resgate: Ingresso de cinema' },
    { raw: 'Credit expired (credits up to 2025-10)', text: 'Créditos expirados (até out/25)' },
    { raw: 'Something new from the ledger', text: 'Something new from the ledger' },
  ])('reads "$raw" as "$text"', ({ raw, text }) => {
    expect(describeDescription(raw)).toBe(text);
  });
});

describe('describeActor', () => {
  it.each<{ kind: ActorKind; id: string | null; perspective: Perspective; label: string }>([
    { kind: 'Member', id: 'abc', perspective: 'member', label: 'Você' },
    { kind: 'Member', id: 'abc', perspective: 'operator', label: 'Membro' },
    { kind: 'PaymentProvider', id: 'pix', perspective: 'member', label: 'PSP Pix' },
    { kind: 'Operator', id: 'ana', perspective: 'member', label: 'Operador ana' },
    { kind: 'System', id: 'credit-expiry', perspective: 'member', label: 'Sistema (expiração)' },
    { kind: 'System', id: null, perspective: 'operator', label: 'Sistema' },
    { kind: 'Anonymous', id: null, perspective: 'member', label: 'Anônimo' },
  ])('names $kind ($id) as "$label" for the $perspective', ({ kind, id, perspective, label }) => {
    expect(describeActor({ actorKind: kind, actorId: id }, perspective)).toBe(label);
  });
});

describe('isReversal', () => {
  it.each([
    { description: 'Reversal of Monthly allowance 2026-10', expected: true },
    { description: 'Monthly allowance 2026-10', expected: false },
  ])('answers $expected for "$description"', ({ description, expected }) => {
    expect(isReversal({ description })).toBe(expected);
  });
});
