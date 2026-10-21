import type { ReactNode } from 'react';
import { ScrollView, type ScrollViewProps, StyleSheet, View, type ViewStyle } from 'react-native';
import { Text } from './text';
import { useColors } from './theme';
import { maxContentWidth, space } from './tokens';

type ScreenProps = {
  readonly children: ReactNode;
  readonly refreshControl?: ScrollViewProps['refreshControl'];
};

// Every screen shares the paper background and a centered reading column on wide (desktop web) windows. Safe areas
// belong to the layouts (app bar on top, tab bar at the bottom), so screens never double them.
export function Screen({ children, refreshControl }: ScreenProps) {
  const colors = useColors();
  return (
    <ScrollView
      style={[styles.fill, { backgroundColor: colors.background }]}
      contentContainerStyle={[columnStyle, styles.scrollContent]}
      keyboardShouldPersistTaps="handled"
      refreshControl={refreshControl}
    >
      {children}
    </ScrollView>
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
