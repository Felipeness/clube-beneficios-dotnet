import type { ReactNode } from 'react';
import { StyleSheet, View, type ViewStyle } from 'react-native';
import { useColors } from './theme';
import { radius, space } from './tokens';

type CardProps = { readonly children: ReactNode; readonly style?: ViewStyle; readonly accessibilityLabel?: string };

export function Card({ children, style, accessibilityLabel }: CardProps) {
  const colors = useColors();
  return (
    <View
      accessible={accessibilityLabel !== undefined}
      accessibilityLabel={accessibilityLabel}
      style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }, style]}
    >
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  card: { borderRadius: radius.lg, borderWidth: 1, padding: space.lg, gap: space.md },
});
