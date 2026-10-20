// Direction: "carteirinha e cupom". The club is a paper membership card and a strip of coupons: warm cream paper, a deep
// bottle green for money and trust, tangerine for the moments of joy (a credit landing, a reward redeemed). Edges are
// round and friendly, numbers are big and confident, nothing is childish.

const palette = {
  cream50: '#FFFBF4',
  cream100: '#FBF4E8',
  cream200: '#F3E8D6',
  cream300: '#E7D9C2',
  green950: '#0A2A21',
  green900: '#0D3B2E',
  green700: '#0F5C46',
  green500: '#1F8A68',
  green300: '#6FCFA9',
  green100: '#D9F2E6',
  tangerine600: '#D9541F',
  tangerine500: '#F26B3A',
  tangerine300: '#FFA47E',
  tangerine100: '#FFE6D9',
  ink900: '#1E2621',
  ink600: '#55615A',
  ink400: '#8A958E',
  red700: '#B3261E',
  red300: '#FF8A80',
  red100: '#FDE3DF',
  amber700: '#8A5A00',
  amber100: '#FFF0C7',
  night950: '#0F1412',
  night900: '#151C19',
  night800: '#1C2521',
  night700: '#26312C',
  night500: '#3A4842',
  paper100: '#F4EDE2',
  paper400: '#AAB5AE',
} as const;

export type ColorTokens = {
  readonly background: string;
  readonly surface: string;
  readonly surfaceSunken: string;
  readonly border: string;
  readonly text: string;
  readonly textMuted: string;
  readonly textFaint: string;
  readonly primary: string;
  readonly primaryPressed: string;
  readonly onPrimary: string;
  readonly primarySoft: string;
  readonly accent: string;
  readonly onAccent: string;
  readonly accentSoft: string;
  readonly card: string;
  readonly onCard: string;
  readonly onCardMuted: string;
  readonly credit: string;
  readonly debit: string;
  readonly danger: string;
  readonly dangerSoft: string;
  readonly warning: string;
  readonly warningSoft: string;
  readonly skeleton: string;
  readonly overlay: string;
};

const light: ColorTokens = {
  background: palette.cream100,
  surface: palette.cream50,
  surfaceSunken: palette.cream200,
  border: palette.cream300,
  text: palette.ink900,
  textMuted: palette.ink600,
  textFaint: palette.ink400,
  primary: palette.green700,
  primaryPressed: palette.green900,
  onPrimary: palette.cream50,
  primarySoft: palette.green100,
  accent: palette.tangerine500,
  onAccent: palette.green950,
  accentSoft: palette.tangerine100,
  card: palette.green700,
  onCard: palette.cream50,
  onCardMuted: palette.green100,
  credit: '#15734F',
  debit: palette.red700,
  danger: palette.red700,
  dangerSoft: palette.red100,
  warning: palette.amber700,
  warningSoft: palette.amber100,
  skeleton: palette.cream300,
  overlay: 'rgba(13, 59, 46, 0.45)',
};

const dark: ColorTokens = {
  background: palette.night950,
  surface: palette.night900,
  surfaceSunken: palette.night800,
  border: palette.night700,
  text: palette.paper100,
  textMuted: palette.paper400,
  textFaint: '#7D8983',
  primary: palette.green300,
  primaryPressed: '#8FE0BF',
  onPrimary: palette.green950,
  primarySoft: '#173A2F',
  accent: palette.tangerine300,
  onAccent: palette.green950,
  accentSoft: '#3A2519',
  card: '#0E4A39',
  onCard: palette.cream50,
  onCardMuted: palette.green100,
  credit: palette.green300,
  debit: palette.red300,
  danger: palette.red300,
  dangerSoft: '#3B1E1B',
  warning: '#FFD166',
  warningSoft: '#3A2F12',
  skeleton: palette.night700,
  overlay: 'rgba(0, 0, 0, 0.6)',
};

export const colorSchemes = { light, dark } as const;

export const space = { xxs: 2, xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32, xxxl: 48 } as const;

export const radius = { sm: 8, md: 14, lg: 22, xl: 30, pill: 999 } as const;

export const fonts = {
  display: 'BricolageGrotesque_700Bold',
  displayHeavy: 'BricolageGrotesque_800ExtraBold',
  body: 'Figtree_400Regular',
  bodyMedium: 'Figtree_500Medium',
  bodySemibold: 'Figtree_600SemiBold',
  bodyBold: 'Figtree_700Bold',
  mono: 'JetBrainsMono_500Medium',
} as const;

// Sizes are the base for a 100% font scale; Text keeps allowFontScaling on, so large system fonts grow them.
export const typeScale = {
  hero: { fontFamily: fonts.displayHeavy, fontSize: 44, lineHeight: 50, letterSpacing: -1 },
  title: { fontFamily: fonts.display, fontSize: 28, lineHeight: 34, letterSpacing: -0.5 },
  heading: { fontFamily: fonts.display, fontSize: 20, lineHeight: 26, letterSpacing: -0.2 },
  body: { fontFamily: fonts.body, fontSize: 16, lineHeight: 23 },
  bodyStrong: { fontFamily: fonts.bodySemibold, fontSize: 16, lineHeight: 23 },
  label: { fontFamily: fonts.bodyBold, fontSize: 13, lineHeight: 18, letterSpacing: 0.6 },
  caption: { fontFamily: fonts.bodyMedium, fontSize: 13, lineHeight: 18 },
  mono: { fontFamily: fonts.mono, fontSize: 13, lineHeight: 18 },
} as const;

export type TypeVariant = keyof typeof typeScale;

// Readable measure on desktop web; phones never reach it.
export const maxContentWidth = 720;

// WCAG target size: anything pressable is at least this tall and wide.
export const minTouchTarget = 48;
