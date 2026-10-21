import { useEffect, useState } from 'react';
import { Easing, useAnimatedReaction, useReducedMotion, useSharedValue, withTiming } from 'react-native-reanimated';
import { scheduleOnRN } from 'react-native-worklets';
import { type Cents, cents, formatMoney } from '@/money/money';
import { Text, type TextProps } from '@/ui/text';

const COUNT_DURATION_MS = 700;

// The balance counts up or down to its new value on the UI thread; only whole cents reach React, so a re-render
// happens per visible change, not per frame. With "reduce motion" on it jumps straight to the value.
function useCountingCents(target: Cents): Cents {
  const isReducedMotion = useReducedMotion();
  const animated = useSharedValue<number>(target);
  const [shown, setShown] = useState<number>(target);

  useEffect(() => {
    animated.set(
      isReducedMotion ? target : withTiming(target, { duration: COUNT_DURATION_MS, easing: Easing.out(Easing.cubic) }),
    );
  }, [animated, isReducedMotion, target]);

  useAnimatedReaction(
    () => Math.round(animated.get()),
    (current, previous) => {
      if (current !== previous) scheduleOnRN(setShown, current);
    },
  );

  return cents(shown);
}

type AnimatedMoneyProps = Omit<TextProps, 'children'> & { readonly amount: Cents };

export function AnimatedMoney({ amount, ...text }: AnimatedMoneyProps) {
  const shown = useCountingCents(amount);
  // Screen readers get the settled value once, not every intermediate number.
  return (
    <Text accessibilityLabel={formatMoney(amount)} {...text}>
      {formatMoney(shown)}
    </Text>
  );
}
