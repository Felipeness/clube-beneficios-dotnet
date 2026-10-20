import { useEffect } from 'react';
import type { DimensionValue } from 'react-native';
import Animated, {
  useAnimatedStyle,
  useReducedMotion,
  useSharedValue,
  withRepeat,
  withTiming,
} from 'react-native-reanimated';
import { useColors } from './theme';
import { radius } from './tokens';

type SkeletonProps = { readonly width?: DimensionValue; readonly height: number; readonly rounded?: number };

export function Skeleton({ width = '100%', height, rounded = radius.sm }: SkeletonProps) {
  const colors = useColors();
  const opacity = useSharedValue(1);
  const isReducedMotion = useReducedMotion();

  useEffect(() => {
    if (isReducedMotion) return;
    opacity.set(withRepeat(withTiming(0.45, { duration: 750 }), -1, true));
  }, [isReducedMotion, opacity]);

  const pulse = useAnimatedStyle(() => ({ opacity: opacity.get() }));
  return (
    <Animated.View
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
      style={[{ width, height, borderRadius: rounded, backgroundColor: colors.skeleton }, pulse]}
    />
  );
}
