// A billing period ("competência") is a calendar month in São Paulo, spelled yyyy-MM like the API expects.

// Brazil has had no daylight saving since 2019, so São Paulo is a fixed UTC-3. Doing the shift by hand keeps the result
// identical on Hermes, the browser and Node, whatever time-zone data each one ships.
const SAO_PAULO_OFFSET_MS = -3 * 60 * 60 * 1000;

const MONTH_NAMES = [
  'janeiro',
  'fevereiro',
  'março',
  'abril',
  'maio',
  'junho',
  'julho',
  'agosto',
  'setembro',
  'outubro',
  'novembro',
  'dezembro',
] as const;

type YearMonth = { readonly year: number; readonly month: number };

function yearMonthAt(instant: Date): YearMonth {
  const local = new Date(instant.getTime() + SAO_PAULO_OFFSET_MS);
  return { year: local.getUTCFullYear(), month: local.getUTCMonth() + 1 };
}

const toPeriod = ({ year, month }: YearMonth): string => `${year}-${String(month).padStart(2, '0')}`;

export const periodAt = (instant: Date): string => toPeriod(yearMonthAt(instant));

// Every period the API accepts for a payment: from the subscription month up to the current one, newest first.
export function payablePeriods(subscribedAt: Date, now: Date): readonly string[] {
  const first = yearMonthAt(subscribedAt);
  const last = yearMonthAt(now);
  const count = (last.year - first.year) * 12 + (last.month - first.month) + 1;
  return Array.from({ length: Math.max(count, 0) }, (_, back) => {
    const monthIndex = last.year * 12 + (last.month - 1) - back;
    return toPeriod({ year: Math.floor(monthIndex / 12), month: (monthIndex % 12) + 1 });
  });
}

function parsePeriod(period: string): YearMonth | null {
  const match = /^(\d{4})-(\d{2})$/.exec(period);
  if (!match) return null;
  const month = Number(match[2]);
  return month >= 1 && month <= 12 ? { year: Number(match[1]), month } : null;
}

export function formatPeriod(period: string): string {
  const parsed = parsePeriod(period);
  const name = parsed ? MONTH_NAMES[parsed.month - 1] : undefined;
  return parsed && name ? `${name} de ${parsed.year}` : period;
}

export function formatPeriodShort(period: string): string {
  const parsed = parsePeriod(period);
  const name = parsed ? MONTH_NAMES[parsed.month - 1] : undefined;
  return parsed && name ? `${name.slice(0, 3)}/${String(parsed.year).slice(2)}` : period;
}

// The São Paulo calendar day as yyyy-MM-dd, the format the reconciliation expects.
export function dayAt(instant: Date): string {
  return new Date(instant.getTime() + SAO_PAULO_OFFSET_MS).toISOString().slice(0, 10);
}
