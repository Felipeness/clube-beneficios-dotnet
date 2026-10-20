import { Text as NativeText, type TextProps as NativeTextProps } from 'react-native';
import { useColors } from './theme';
import type { ColorTokens, TypeVariant } from './tokens';
import { typeScale } from './tokens';

type Tone = keyof ColorTokens;

export type TextProps = NativeTextProps & {
  readonly variant?: TypeVariant;
  readonly tone?: Tone;
};

// Large display sizes stop growing at 1.6x so a hero number never pushes the layout off screen; body text scales freely.
const maxScaleFor: Partial<Record<TypeVariant, number>> = { hero: 1.6, title: 1.8 };

export function Text({ variant = 'body', tone = 'text', style, ...props }: TextProps) {
  const colors = useColors();
  return (
    <NativeText
      maxFontSizeMultiplier={maxScaleFor[variant]}
      style={[typeScale[variant], { color: colors[tone] }, style]}
      {...props}
    />
  );
}
