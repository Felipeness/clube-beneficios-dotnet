import { StyleSheet, View } from 'react-native';
import Animated, { FadeInDown } from 'react-native-reanimated';
import { formatMoney } from '@/money/money';
import { Button } from '@/ui/button';
import { Card } from '@/ui/card';
import { Notice } from '@/ui/notice';
import { Text } from '@/ui/text';
import { useColors } from '@/ui/theme';
import { radius, space } from '@/ui/tokens';
import { presentReward } from './reward-catalog';
import type { Delivery, RedemptionAttempt } from './use-redemption';

type RedemptionReceiptProps = {
  readonly attempt: RedemptionAttempt;
  readonly isSending: boolean;
  readonly onRepeat: () => void;
};

// The idempotency demo, in plain sight: the key, every time it was sent, and the balance after each send.
export function RedemptionReceipt({ attempt, isSending, onRepeat }: RedemptionReceiptProps) {
  const colors = useColors();
  const rewardName = presentReward(attempt.request.rewardId, attempt.request.rewardId).name;
  const latest = attempt.deliveries.at(-1);

  return (
    <Card>
      <Text variant="heading">Pedido de resgate: {rewardName}</Text>
      <View style={[styles.key, { backgroundColor: colors.surfaceSunken }]}>
        <Text variant="label" tone="textMuted">
          IDEMPOTENCY-KEY
        </Text>
        <Text variant="mono" selectable>
          {attempt.request.idempotencyKey}
        </Text>
      </View>
      {latest && <LatestOutcome latest={latest} sends={attempt.deliveries.length} />}
      {attempt.deliveries.length > 1 && (
        <View style={styles.log} accessibilityLabel="Histórico de envios">
          {attempt.deliveries.map((delivery) => (
            <Animated.View key={delivery.sequence} entering={FadeInDown.duration(200)} style={styles.logRow}>
              <Text variant="caption" tone="textMuted">
                {delivery.sequence}º envio
              </Text>
              <Text variant="caption">{summarize(delivery)}</Text>
            </Animated.View>
          ))}
        </View>
      )}
      <Button
        label="Repetir a mesma requisição"
        icon="repeat"
        variant="secondary"
        onPress={onRepeat}
        isLoading={isSending}
        accessibilityHint="Reenvia o mesmo pedido com a mesma Idempotency-Key"
      />
    </Card>
  );
}

function LatestOutcome({ latest, sends }: { readonly latest: Delivery; readonly sends: number }) {
  if (latest.outcome === 'refused') return <Notice tone="error" {...latest.error} />;
  if (sends === 1) {
    return <Notice tone="success" title="Resgatado!" detail={`Seu saldo agora é ${formatMoney(latest.balance)}.`} />;
  }
  return (
    <Notice
      tone="info"
      title="Mesma chave, mesmo resultado"
      detail={`O servidor reconheceu a Idempotency-Key e devolveu o resgate original, sem cobrar de novo. Saldo: ${formatMoney(latest.balance)}.`}
    />
  );
}

const summarize = (delivery: Delivery): string =>
  delivery.outcome === 'redeemed' ? `saldo depois: ${formatMoney(delivery.balance)}` : delivery.error.title;

const styles = StyleSheet.create({
  key: { padding: space.md, borderRadius: radius.md, gap: space.xxs },
  log: { gap: space.xs },
  logRow: { flexDirection: 'row', justifyContent: 'space-between', gap: space.md },
});
