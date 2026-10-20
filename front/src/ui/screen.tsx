import type { ReactNode } from 'react';
import { ScrollView, StyleSheet, View, type ViewStyle } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Text } from './text';
import { useColors } from './theme';
import { maxContentWidth, space } from './tokens';

type ScreenProps = {
  readonly children: ReactNode;
  readonly scrolls?: boolean;
};

// Every screen shares the same paper background, safe areas and a centered reading column on wide (desktop web) windows.
export function Screen({ children, scrolls = true }: ScreenProps) {
  const colors = useColors();
  const background = { backgroundColor: colors.background };
  if (!scrolls) {
    return (
      <SafeAreaView edges={['top']} style={[styles.fill, background]}>
        <View style={[styles.fill, columnStyle]}>{children}</View>
      </SafeAreaView>
    );
  }
  return (
    <SafeAreaView edges={['top']} style={[styles.fill, background]}>
      <ScrollView contentContainerStyle={[columnStyle, styles.scrollContent]} keyboardShouldPersistTaps="handled">
        {children}
      </ScrollView>
    </SafeAreaView>
  );
}

export const columnStyle: ViewStyle = {
  width: '100%',
  maxWidth: maxContentWidth,
  alignSelf: 'center',
  paddingHorizontal: space.lg,
};

type ScreenHeaderProps = { readonly eyebrow?: string; readonly title: string; readonly subtitle?: string };

export function ScreenHeader({ eyebrow, title, subtitle }: ScreenHeaderProps) {
  return (
    <View style={styles.header}>
      {eyebrow && (
        <Text variant="label" tone="accent" style={styles.eyebrow}>
          {eyebrow.toUpperCase()}
        </Text>
      )}
      <Text variant="title" accessibilityRole="header">
        {title}
      </Text>
      {subtitle && <Text tone="textMuted">{subtitle}</Text>}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  scrollContent: { paddingBottom: space.xxxl * 2, gap: space.lg },
  header: { paddingTop: space.xl, paddingBottom: space.sm, gap: space.xs },
  eyebrow: { marginBottom: space.xxs },
});
