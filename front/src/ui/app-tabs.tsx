import MaterialCommunityIcons from '@expo/vector-icons/MaterialCommunityIcons';
import type { Href } from 'expo-router';
import { TabList, TabSlot, Tabs, TabTrigger, type TabTriggerSlotProps } from 'expo-router/ui';
import { Pressable, StyleSheet, useWindowDimensions, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useSession } from '@/session/session-provider';
import { Button, type IconName } from './button';
import { Text } from './text';
import { useColors } from './theme';
import { maxContentWidth, minTouchTarget, radius, space } from './tokens';

export type TabSpec = { readonly name: string; readonly href: Href; readonly label: string; readonly icon: IconName };

type AppTabsProps = { readonly tabs: readonly TabSpec[]; readonly audience: string };

const WIDE_BREAKPOINT = 768;
const APP_BAR_HEIGHT = 64;

// One navigation shell for both audiences: on a phone the tabs sit at the bottom, under the thumb; on a wide browser
// window they move into the app bar, where a desktop reader looks for them.
export function AppTabs({ tabs, audience }: AppTabsProps) {
  const colors = useColors();
  const insets = useSafeAreaInsets();
  const isWide = useWindowDimensions().width >= WIDE_BREAKPOINT;

  return (
    <Tabs style={[styles.fill, { backgroundColor: colors.background }]}>
      <AppBar audience={audience} topInset={insets.top} />
      <TabSlot style={styles.fill} />
      <TabList asChild>
        <View
          accessibilityRole="tablist"
          style={
            isWide
              ? [styles.inlineTabs, { top: insets.top }]
              : [
                  styles.bottomTabs,
                  { paddingBottom: Math.max(insets.bottom, space.sm), backgroundColor: colors.surface },
                  { borderTopColor: colors.border },
                ]
          }
        >
          {tabs.map((tab) => (
            <TabTrigger key={tab.name} name={tab.name} href={tab.href} asChild>
              <TabButton label={tab.label} icon={tab.icon} isWide={isWide} />
            </TabTrigger>
          ))}
        </View>
      </TabList>
    </Tabs>
  );
}

function AppBar({ audience, topInset }: { readonly audience: string; readonly topInset: number }) {
  const colors = useColors();
  const { signOut } = useSession();
  return (
    <View style={[styles.appBar, { paddingTop: topInset, borderBottomColor: colors.border }]}>
      <View style={styles.appBarRow}>
        <View style={styles.brand}>
          <View style={[styles.brandMark, { backgroundColor: colors.primary }]}>
            <View style={[styles.brandDot, { backgroundColor: colors.accent }]} />
          </View>
          <Text variant="heading" accessibilityRole="header">
            Clube
          </Text>
          <View style={[styles.audience, { backgroundColor: colors.accentSoft }]}>
            <Text variant="caption" tone="text" numberOfLines={1}>
              {audience}
            </Text>
          </View>
        </View>
        <Button label="Sair" icon="logout" variant="ghost" onPress={() => void signOut()} compact />
      </View>
    </View>
  );
}

type TabButtonProps = TabTriggerSlotProps & {
  readonly label: string;
  readonly icon: IconName;
  readonly isWide: boolean;
};

function TabButton({ label, icon, isWide, isFocused = false, ...trigger }: TabButtonProps) {
  const colors = useColors();
  const tint = isFocused ? colors.primary : colors.textMuted;
  return (
    <Pressable
      {...trigger}
      accessibilityRole="tab"
      accessibilityLabel={label}
      accessibilityState={{ selected: isFocused }}
      style={({ pressed }) => [
        isWide ? styles.wideTab : styles.narrowTab,
        isWide && isFocused && { backgroundColor: colors.primarySoft },
        { opacity: pressed ? 0.7 : 1 },
      ]}
    >
      <MaterialCommunityIcons name={icon} size={isWide ? 18 : 24} color={tint} />
      <Text variant="caption" style={{ color: tint }} numberOfLines={1}>
        {label}
      </Text>
      {!isWide && isFocused && <View style={[styles.focusBar, { backgroundColor: colors.accent }]} />}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  appBar: { borderBottomWidth: StyleSheet.hairlineWidth },
  appBarRow: {
    height: APP_BAR_HEIGHT,
    width: '100%',
    maxWidth: maxContentWidth + 280,
    alignSelf: 'center',
    paddingHorizontal: space.lg,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  brand: { flexDirection: 'row', alignItems: 'center', gap: space.sm, flexShrink: 1 },
  brandMark: { width: 30, height: 22, borderRadius: 6, alignItems: 'center', justifyContent: 'center' },
  brandDot: { width: 10, height: 10, borderRadius: radius.pill },
  audience: { paddingHorizontal: space.sm, paddingVertical: space.xxs, borderRadius: radius.pill, flexShrink: 1 },
  inlineTabs: {
    position: 'absolute',
    alignSelf: 'center',
    height: APP_BAR_HEIGHT,
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.xs,
  },
  wideTab: {
    minHeight: minTouchTarget - 8,
    paddingHorizontal: space.lg,
    borderRadius: radius.pill,
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.sm,
  },
  bottomTabs: { flexDirection: 'row', borderTopWidth: StyleSheet.hairlineWidth, paddingTop: space.xs },
  narrowTab: { flex: 1, minHeight: minTouchTarget + 8, alignItems: 'center', justifyContent: 'center', gap: 2 },
  focusBar: { position: 'absolute', top: -space.xs, width: 28, height: 3, borderRadius: radius.pill },
});
