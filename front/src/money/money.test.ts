import { cents, formatMoney, formatSignedMoney } from './money';

// Intl separates the symbol with a no-break space; normalizing keeps the table readable.
const plain = (text: string) => text.replace(/ /g, ' ');

describe('formatMoney', () => {
  it.each([
    { amount: 0, text: 'R$ 0,00' },
    { amount: 1, text: 'R$ 0,01' },
    { amount: 2000, text: 'R$ 20,00' },
    { amount: 2541, text: 'R$ 25,41' },
    { amount: 2990, text: 'R$ 29,90' },
    { amount: 123456789, text: 'R$ 1.234.567,89' },
    { amount: -1000, text: '-R$ 10,00' },
  ])('formats $amount cents as "$text"', ({ amount, text }) => {
    expect(plain(formatMoney(cents(amount)))).toBe(text);
  });
});

describe('formatSignedMoney', () => {
  it.each([
    { amount: 2000, text: '+ R$ 20,00' },
    { amount: -2541, text: '− R$ 25,41' },
    { amount: 0, text: 'R$ 0,00' },
  ])('formats $amount cents as "$text"', ({ amount, text }) => {
    expect(plain(formatSignedMoney(cents(amount)))).toBe(text);
  });
});

describe('cents', () => {
  it('rejects fractional cents', () => {
    expect(() => cents(10.5)).toThrow();
  });
});
