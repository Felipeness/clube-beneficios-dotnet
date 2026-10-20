import MaterialCommunityIcons from '@expo/vector-icons/MaterialCommunityIcons';
import { StyleSheet, View } from 'react-native';
import { Button, type IconName } from './button';
import { Text } from './text';
import { useColors } from './theme';
import { radius, space } from './tokens';

type EmptyStateProps = { readonly icon: IconName; readonly title: string; readonly detail: string };

export function EmptyState({ icon, title, detail }: EmptyStateProps) {
  const colors = useColors();
  return (
    <View style={styles.state} accessible accessibilityLabel={`${title}. ${detail}`}>
      <View style={[styles.badge, { backgroundColor: colors.accentSoft }]}>
        <MaterialCommunityIcons name={icon} size={30} color={colors.accent} />
      </View>
      <Text variant="heading" style={styles.centered}>
        {title}
      </Text>
      <Text tone="textMuted" style={styles.centered}>
        {detail}
      </Text>
    </View>
  );
}

type ErrorStateProps = { readonly title: string; readonly detail: string; readonly onRetry: () => void };

export function ErrorState({ title, detail, onRetry }: ErrorStateProps) {
  const colors = useColors();
  return (
    <View style={styles.state} accessibilityRole="alert">
      <View style={[styles.badge, { backgroundColor: colors.dangerSoft }]}>
        <MaterialCommunityIcons name="cloud-alert-outline" size={30} color={colors.danger} />
      </View>
      <Text variant="heading" style={styles.centered}>
        {title}
      </Text>
      <Text tone="textMuted" style={styles.centered}>
        {detail}
      </Text>
      <Button label="Tentar de novo" icon="refresh" variant="secondary" onPress={onRetry} compact />
    </View>
  );
}

const styles = StyleSheet.create({
  state: { alignItems: 'center', gap: space.md, paddingVertical: space.xxl, paddingHorizontal: space.lg },
  badge: { width: 64, height: 64, borderRadius: radius.pill, alignItems: 'center', justifyContent: 'center' },
  centered: { textAlign: 'center' },
});
