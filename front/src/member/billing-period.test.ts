import { dayAt, formatPeriod, formatPeriodShort, payablePeriods, periodAt } from './billing-period';

describe('periodAt', () => {
  it.each([
    { instant: '2026-10-20T15:00:00Z', period: '2026-10' },
    { instant: '2026-11-01T02:59:59Z', period: '2026-10' },
    { instant: '2026-11-01T03:00:00Z', period: '2026-11' },
    { instant: '2027-01-01T01:00:00Z', period: '2026-12' },
  ])('places $instant in $period (São Paulo)', ({ instant, period }) => {
    expect(periodAt(new Date(instant))).toBe(period);
  });
});

describe('payablePeriods', () => {
  it.each([
    { subscribedAt: '2026-10-05T12:00:00Z', now: '2026-10-20T12:00:00Z', periods: ['2026-10'] },
    {
      subscribedAt: '2026-11-15T12:00:00Z',
      now: '2027-02-01T12:00:00Z',
      periods: ['2027-02', '2027-01', '2026-12', '2026-11'],
    },
    { subscribedAt: '2026-10-05T12:00:00Z', now: '2026-09-01T12:00:00Z', periods: [] },
  ])('lists from $subscribedAt to $now, newest first', ({ subscribedAt, now, periods }) => {
    expect(payablePeriods(new Date(subscribedAt), new Date(now))).toEqual(periods);
  });
});

describe('formatPeriod', () => {
  it.each([
    { period: '2026-10', long: 'outubro de 2026', short: 'out/26' },
    { period: '2027-03', long: 'março de 2027', short: 'mar/27' },
    { period: '2026-13', long: '2026-13', short: '2026-13' },
    { period: 'junk', long: 'junk', short: 'junk' },
  ])('spells $period as "$long" and "$short"', ({ period, long, short }) => {
    expect(formatPeriod(period)).toBe(long);
    expect(formatPeriodShort(period)).toBe(short);
  });
});

describe('dayAt', () => {
  it.each([
    { instant: '2026-10-20T15:00:00Z', day: '2026-10-20' },
    { instant: '2026-10-21T02:59:59Z', day: '2026-10-20' },
    { instant: '2026-10-21T03:00:00Z', day: '2026-10-21' },
  ])('places $instant on $day (São Paulo)', ({ instant, day }) => {
    expect(dayAt(new Date(instant))).toBe(day);
  });
});
