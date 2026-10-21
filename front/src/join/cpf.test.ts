import { generateDemoCpf, hasCpfLength, maskCpf } from './cpf';

describe('maskCpf', () => {
  it.each([
    { typed: '', masked: '' },
    { typed: '5', masked: '5' },
    { typed: '529', masked: '529' },
    { typed: '5299', masked: '529.9' },
    { typed: '529982', masked: '529.982' },
    { typed: '5299822', masked: '529.982.2' },
    { typed: '529982247', masked: '529.982.247' },
    { typed: '5299822472', masked: '529.982.247-2' },
    { typed: '52998224725', masked: '529.982.247-25' },
    { typed: '529982247250000', masked: '529.982.247-25' },
    { typed: '529.982.247-25', masked: '529.982.247-25' },
    { typed: 'abc529 982x247', masked: '529.982.247' },
  ])('formats "$typed" as "$masked"', ({ typed, masked }) => {
    expect(maskCpf(typed)).toBe(masked);
  });
});

describe('hasCpfLength', () => {
  it.each([
    { text: '529.982.247-25', expected: true },
    { text: '52998224725', expected: true },
    { text: '529.982.247-2', expected: false },
    { text: '', expected: false },
  ])('answers $expected for "$text"', ({ text, expected }) => {
    expect(hasCpfLength(text)).toBe(expected);
  });
});

describe('generateDemoCpf', () => {
  it('computes both check digits of a known valid CPF', () => {
    const digits = [5, 2, 9, 9, 8, 2, 2, 4, 7];
    let next = 0;
    const replay = () => digits[next++] ?? 0;

    expect(generateDemoCpf(replay)).toBe('529.982.247-25');
  });

  it('turns a remainder of ten into a zero check digit', () => {
    // 111.111.112-00 is valid only because the first remainder is 10.
    const digits = [1, 1, 1, 1, 1, 1, 1, 1, 2];
    let next = 0;

    expect(generateDemoCpf(() => digits[next++] ?? 0)).toMatch(/^111\.111\.112-0\d$/);
  });
});
