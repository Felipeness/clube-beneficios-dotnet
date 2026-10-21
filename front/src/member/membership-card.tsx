import MaterialCommunityIcons from '@expo/vector-icons/MaterialCommunityIcons';
import * as Clipboard from 'expo-clipboard';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import Animated, { FadeInUp } from 'react-native-reanimated';
import type { Membership } from '@/api/schemas';
import { type Cents, cents, formatSignedMoney } from '@/money/money';
import { Text } from '@/ui/text';
import { useColors } from '@/ui/theme';
import { minTouchTarget, radius, space } from '@/ui/tokens';
import { AnimatedMoney } from './animated-money';
import { formatPeriod, periodAt } from './billing-period';

// Remembers how much the balance moved last time it changed, adjusting state during render (the React way to derive
// from a changing prop) instead of syncing it in an effect.
function useLastChange(balance: Cents): Cents | null {
  const [previous, setPrevious] = useState(balance);
  const [change, setChange] = useState<Cents | null>(null);
  if (balance !== previous) {
    setPrevious(balance);
    setChange(cents(balance - previous));
  }
  return change;
}

export function MembershipCard({ membership }: { readonly membership: Membership }) {
  const colors = useColors();
  const change = useLastChange(membership.balanceCents);
  return (
    <View style={[styles.card, { backgroundColor: colors.card }]} accessibilityLiveRegion="polite">
      <View style={[styles.sun, { backgroundColor: colors.accent }]} />
      <View style={styles.topRow}>
        <Text variant="label" tone="onCardMuted">
          CARTEIRINHA
        </Text>
      </View>
      <View style={styles.balance}>
        <Text tone="onCardMuted">Saldo disponível</Text>
        <AnimatedMoney amount={membership.balanceCents} variant="hero" tone="onCard" />
        {change !== null && change !== 0 && (
          <Animated.View
            key={membership.balanceCents}
            entering={FadeInUp.duration(320)}
            style={[styles.change, { backgroundColor: change > 0 ? colors.accent : colors.dangerSoft }]}
          >
            <Text variant="caption" style={{ color: change > 0 ? colors.onAccent : colors.danger }}>
              {formatSignedMoney(change)} agora há pouco
            </Text>
          </Animated.View>
        )}
      </View>
      <View style={styles.bottomRow}>
        <View style={styles.holder}>
          <Text variant="caption" tone="onCardMuted">
            Titular
          </Text>
          <Text variant="mono" tone="onCard">
            {membership.holder}
          </Text>
          <Text variant="caption" tone="onCardMuted">
            Membro desde {formatPeriod(periodAt(membership.subscribedAt))}
          </Text>
        </View>
        <MembershipCode id={membership.id} />
      </View>
    </View>
  );
}

function MembershipCode({ id }: { readonly id: string }) {
  const colors = useColors();
  const [hasCopied, setHasCopied] = useState(false);
  const copy = () => {
    Clipboard.setStringAsync(id)
      .then(() => setHasCopied(true))
      .catch((error: unknown) => console.warn('clipboard: copy failed', error));
  };
  return (
    <Pressable
      onPress={copy}
      accessibilityRole="button"
      accessibilityLabel={hasCopied ? 'Código da adesão copiado' : 'Copiar código da adesão'}
      accessibilityHint="Use o código para entrar de novo como membro"
      style={({ pressed }) => [styles.code, { borderColor: colors.onCardMuted, opacity: pressed ? 0.7 : 1 }]}
    >
      <Text variant="mono" tone="onCard" numberOfLines={1} style={styles.codeText}>
        {id.slice(0, 8)}…
      </Text>
      <MaterialCommunityIcons name={hasCopied ? 'check' : 'content-copy'} size={16} color={colors.onCard} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: { borderRadius: radius.xl, padding: space.xl, gap: space.lg, overflow: 'hidden', marginTop: space.lg },
  sun: { position: 'absolute', width: 180, height: 180, borderRadius: 90, right: -60, top: -70, opacity: 0.9 },
  topRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: space.sm },
  balance: { gap: space.xs, alignItems: 'flex-start' },
  change: { paddingHorizontal: space.md, paddingVertical: space.xs, borderRadius: radius.pill },
  bottomRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-end',
    gap: space.md,
    flexWrap: 'wrap',
  },
  holder: { gap: space.xxs },
  code: {
    minHeight: minTouchTarget,
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.sm,
    paddingHorizontal: space.md,
    borderRadius: radius.pill,
    borderWidth: 1,
  },
  codeText: { maxWidth: 140 },
});
