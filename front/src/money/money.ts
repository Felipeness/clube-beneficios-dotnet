import { z } from 'zod';

// Money crosses the API as integer cents and stays that way: the only division by 100 is inside the formatter.
export const Cents = z.number().int().brand<'Cents'>();
export type Cents = z.infer<typeof Cents>;

export const cents = (value: number): Cents => Cents.parse(value);

const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

export function formatMoney(amount: Cents): string {
  return brl.format(amount / 100);
}

// Statement lines carry their direction in the sign; the UI shows it explicitly instead of a bare minus.
export function formatSignedMoney(amount: Cents): string {
  if (amount === 0) return formatMoney(amount);
  const sign = amount > 0 ? '+' : '−';
  return `${sign} ${brl.format(Math.abs(amount) / 100)}`;
}
