import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { payPix } from '@/api/clube-api';
import { describeError } from '@/api/error-messages';
import { queryKeys } from '@/api/query-client';
import type { Membership, PixPaymentResult, SimulatedPixPayment } from '@/api/schemas';
import { cents, formatMoney } from '@/money/money';
import { Button } from '@/ui/button';
import { Card } from '@/ui/card';
import { Chip } from '@/ui/chip';
import { confirmWithHaptics } from '@/ui/haptics';
import { Notice, type NoticeTone } from '@/ui/notice';
import { Text } from '@/ui/text';
import { space } from '@/ui/tokens';
import { formatPeriod, formatPeriodShort, payablePeriods, periodAt } from './billing-period';

const MONTHLY_ALLOWANCE = cents(2000);

type Outcome = { readonly tone: NoticeTone; readonly title: string; readonly detail: string };

// The three answers of the Pix webhook, each one a different lesson about idempotency.
const outcomes: Record<PixPaymentResult, (period: string) => Outcome> = {
  credited: (period) => ({
    tone: 'success',
    title: `${formatMoney(MONTHLY_ALLOWANCE)} entraram no seu saldo`,
    detail: `Mensalidade de ${formatPeriod(period)} creditada.`,
  }),
  already_credited: (period) => ({
    tone: 'info',
    title: 'Esse Pix já estava creditado',
    detail: `O PSP reenviou o mesmo pagamento de ${formatPeriod(period)} e o crédito não foi lançado duas vezes.`,
  }),
  duplicate_payment: (period) => ({
    tone: 'warning',
    title: `${formatPeriod(period)} já estava paga`,
    detail: 'Um segundo Pix para a mesma competência não vira crédito. O valor fica marcado para devolução.',
  }),
};

type PixCardProps = { readonly membership: Membership; readonly now: Date };

export function PixCard({ membership, now }: PixCardProps) {
  const queryClient = useQueryClient();
  const currentPeriod = periodAt(now);
  const periods = payablePeriods(membership.subscribedAt, now);
  const [chosenPeriod, setChosenPeriod] = useState<string | null>(null);
  const period = chosenPeriod !== null && periods.includes(chosenPeriod) ? chosenPeriod : currentPeriod;

  const pay = useMutation({
    mutationFn: (paidPeriod: string) => payPix(membership.id, paidPeriod),
    onSuccess: (payment) => {
      confirmWithHaptics(payment.result === 'credited' ? 'success' : 'warning');
      return queryClient.invalidateQueries({ queryKey: queryKeys.membership(membership.id) });
    },
    onError: () => confirmWithHaptics('error'),
  });

  const label = period === currentPeriod ? 'Pagar Pix do mês' : `Pagar Pix de ${formatPeriodShort(period)}`;

  return (
    <Card>
      <View style={styles.heading}>
        <Text variant="heading">Pix da mensalidade</Text>
        <Text tone="textMuted">
          {formatMoney(MONTHLY_ALLOWANCE)} por competência, creditados assim que o PSP confirma o pagamento.
        </Text>
      </View>
      {periods.length > 1 && (
        <View style={styles.periods}>
          <Text variant="label" tone="textMuted">
            COMPETÊNCIA
          </Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} accessibilityRole="radiogroup">
            <View style={styles.chips}>
              {periods.map((option) => (
                <Chip
                  key={option}
                  label={formatPeriodShort(option)}
                  accessibilityLabel={formatPeriod(option)}
                  isSelected={option === period}
                  onPress={() => setChosenPeriod(option)}
                />
              ))}
            </View>
          </ScrollView>
        </View>
      )}
      <Button
        label={label}
        icon="qrcode"
        variant="accent"
        onPress={() => pay.mutate(period)}
        isLoading={pay.isPending}
      />
      {pay.data && pay.variables && <PixOutcome payment={pay.data} period={pay.variables} />}
      {pay.isError && <Notice tone="error" {...describeError(pay.error)} />}
    </Card>
  );
}

function PixOutcome({ payment, period }: { readonly payment: SimulatedPixPayment; readonly period: string }) {
  return (
    <Notice key={payment.endToEndId} {...outcomes[payment.result](period)}>
      <Text variant="mono" tone="textMuted" selectable>
        E2E {payment.endToEndId}
      </Text>
    </Notice>
  );
}

const styles = StyleSheet.create({
  heading: { gap: space.xs },
  periods: { gap: space.sm },
  chips: { flexDirection: 'row', gap: space.sm },
});
