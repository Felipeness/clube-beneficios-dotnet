import MaterialCommunityIcons from '@expo/vector-icons/MaterialCommunityIcons';
import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import Animated, { FadeInDown } from 'react-native-reanimated';
import type { IconName } from './button';
import { Text } from './text';
import { useColors } from './theme';
import { type ColorTokens, radius, space } from './tokens';

export type NoticeTone = 'success' | 'info' | 'warning' | 'error';

type Look = { readonly icon: IconName; readonly background: string; readonly foreground: string };

const looks: Record<NoticeTone, (colors: ColorTokens) => Look> = {
  success: (colors) => ({ icon: 'check-decagram', background: colors.primarySoft, foreground: colors.credit }),
  info: (colors) => ({ icon: 'information-outline', background: colors.surfaceSunken, foreground: colors.text }),
  warning: (colors) => ({ icon: 'alert-outline', background: colors.warningSoft, foreground: colors.warning }),
  error: (colors) => ({ icon: 'alert-circle-outline', background: colors.dangerSoft, foreground: colors.danger }),
};

type NoticeProps = {
  readonly tone: NoticeTone;
  readonly title: string;
  readonly detail?: string;
  readonly children?: ReactNode;
};

// Results and errors are announced to screen readers as they appear (live region), not only shown.
export function Notice({ tone, title, detail, children }: NoticeProps) {
  const look = looks[tone](useColors());
  return (
    <Animated.View
      entering={FadeInDown.duration(220)}
      accessibilityRole={tone === 'error' ? 'alert' : 'summary'}
      accessibilityLiveRegion="polite"
      style={[styles.notice, { backgroundColor: look.background }]}
    >
      <MaterialCommunityIcons name={look.icon} size={22} color={look.foreground} />
      <View style={styles.body}>
        <Text variant="bodyStrong" style={{ color: look.foreground }}>
          {title}
        </Text>
        {detail && <Text tone="textMuted">{detail}</Text>}
        {children}
      </View>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  notice: { flexDirection: 'row', gap: space.md, padding: space.lg, borderRadius: radius.md, alignItems: 'flex-start' },
  body: { flex: 1, gap: space.xs },
});
