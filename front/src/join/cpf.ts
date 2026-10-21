const CPF_DIGITS = 11;

export const onlyDigits = (text: string): string => text.replace(/\D/g, '');

// Formats as the person types (000.000.000-00), ignoring anything that is not a digit, so paste works too.
export function maskCpf(text: string): string {
  const digits = onlyDigits(text).slice(0, CPF_DIGITS);
  const parts = [digits.slice(0, 3), digits.slice(3, 6), digits.slice(6, 9)].filter((part) => part.length > 0);
  const body = parts.join('.');
  const check = digits.slice(9);
  return check.length > 0 ? `${body}-${check}` : body;
}

export const hasCpfLength = (text: string): boolean => onlyDigits(text).length === CPF_DIGITS;

// Demo convenience only: a CPF with valid check digits, so trying the sign-up does not need a real person's number.
// The server still validates it; this mirrors the public algorithm and is not a second source of truth.
export function generateDemoCpf(randomDigit: () => number): string {
  const base = Array.from({ length: 9 }, randomDigit);
  const first = checkDigit(base);
  const second = checkDigit([...base, first]);
  return maskCpf([...base, first, second].join(''));
}

function checkDigit(digits: readonly number[]): number {
  const weightStart = digits.length + 1;
  const sum = digits.reduce((total, digit, index) => total + digit * (weightStart - index), 0);
  const rest = (sum * 10) % 11;
  return rest === 10 ? 0 : rest;
}
