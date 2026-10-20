import MaterialCommunityIcons from '@expo/vector-icons/MaterialCommunityIcons';
import type { ComponentProps } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import Animated, { useAnimatedStyle, useReducedMotion, useSharedValue, withSpring } from 'react-native-reanimated';
import { Text } from './text';
import { useColors } from './theme';
import { type ColorTokens, minTouchTarget, radius, space } from './tokens';

export type IconName = ComponentProps<typeof MaterialCommunityIcons>['name'];

type Variant = 'primary' | 'accent' | 'secondary' | 'ghost' | 'danger';

type ButtonProps = {
  readonly label: string;
  readonly onPress: () => void;
  readonly variant?: Variant;
  readonly icon?: IconName;
  readonly isLoading?: boolean;
  readonly isDisabled?: boolean;
  readonly accessibilityHint?: string;
  readonly compact?: boolean;
};

type VariantStyle = { readonly background: string; readonly foreground: string; readonly border: string };

const variantStyles: Record<Variant, (colors: ColorTokens) => VariantStyle> = {
  primary: (colors) => ({ background: colors.primary, foreground: colors.onPrimary, border: colors.primary }),
  accent: (colors) => ({ background: colors.accent, foreground: colors.onAccent, border: colors.accent }),
  secondary: (colors) => ({ background: colors.surface, foreground: colors.text, border: colors.border }),
  ghost: (colors) => ({ background: 'transparent', foreground: colors.primary, border: 'transparent' }),
  danger: (colors) => ({ background: colors.dangerSoft, foreground: colors.danger, border: colors.dangerSoft }),
};

const AnimatedPressable = Animated.createAnimatedComponent(Pressable);

export function Button({
  label,
  onPress,
  variant = 'primary',
  icon,
  isLoading = false,
  isDisabled = false,
  accessibilityHint,
  compact = false,
}: ButtonProps) {
  const look = variantStyles[variant](useColors());
  const scale = useSharedValue(1);
  const isReducedMotion = useReducedMotion();
  const pressStyle = useAnimatedStyle(() => ({ transform: [{ scale: scale.get() }] }));
  const isInert = isDisabled || isLoading;

  const pressTo = (value: number) => {
    if (isReducedMotion) return;
    scale.set(withSpring(value, { damping: 18, stiffness: 320 }));
  };

  return (
    <AnimatedPressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ disabled: isInert, busy: isLoading }}
      disabled={isInert}
      onPress={onPress}
      onPressIn={() => pressTo(0.96)}
      onPressOut={() => pressTo(1)}
      style={[
        styles.base,
        compact && styles.compact,
        { backgroundColor: look.background, borderColor: look.border, opacity: isDisabled ? 0.5 : 1 },
        pressStyle,
      ]}
    >
      <View style={styles.content}>
        {isLoading ? (
          <ActivityIndicator color={look.foreground} />
        ) : (
          icon && <MaterialCommunityIcons name={icon} size={20} color={look.foreground} />
        )}
        <Text variant="bodyStrong" style={{ color: look.foreground }}>
          {label}
        </Text>
      </View>
    </AnimatedPressable>
  );
}

const styles = StyleSheet.create({
  base: {
    minHeight: minTouchTarget + 4,
    minWidth: minTouchTarget,
    paddingHorizontal: space.xl,
    borderRadius: radius.pill,
    borderWidth: 1.5,
    justifyContent: 'center',
  },
  compact: { minHeight: minTouchTarget, paddingHorizontal: space.lg },
  content: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: space.sm },
});
