import MaterialCommunityIcons from '@expo/vector-icons/MaterialCommunityIcons';
import { useQuery } from '@tanstack/react-query';
import { StyleSheet, View } from 'react-native';
import Animated, { FadeInDown } from 'react-native-reanimated';
import { listRewards } from '@/api/clube-api';
import { describeError } from '@/api/error-messages';
import { queryKeys } from '@/api/query-client';
import type { Reward } from '@/api/schemas';
import { useMembershipQuery } from '@/member/member-queries';
import { type Cents, cents, formatMoney } from '@/money/money';
import { useMemberSession } from '@/session/session-provider';
import { Button } from '@/ui/button';
import { Screen, ScreenHeader } from '@/ui/screen';
import { Skeleton } from '@/ui/skeleton';
import { EmptyState, ErrorState } from '@/ui/state-views';
import { Text } from '@/ui/text';
import { useColors } from '@/ui/theme';
import { Ticket } from '@/ui/ticket';
import { radius, space } from '@/ui/tokens';
import { RedemptionReceipt } from './redemption-receipt';
import { presentReward } from './reward-catalog';
import { useRedemption } from './use-redemption';

export function RewardsScreen() {
  const session = useMemberSession();
  const membership = useMembershipQuery(session.membershipId, session.token);
  // The catalog changes only with a deploy, so it stays fresh for the whole session.
  const rewards = useQuery({
    queryKey: queryKeys.rewards,
    queryFn: ({ signal }) => listRewards({ signal }),
    staleTime: Infinity,
  });
  const redemption = useRedemption(session);

  return (
    <Screen>
      <ScreenHeader
        eyebrow="Recompensas"
        title="Troque seu crédito"
        subtitle={membership.data ? `Seu saldo: ${formatMoney(membership.data.balanceCents)}` : 'Escolha um cupom.'}
      />
      {redemption.attempt && (
        <RedemptionReceipt attempt={redemption.attempt} isSending={redemption.isSending} onRepeat={redemption.repeat} />
      )}
      {rewards.isPending && <CatalogSkeleton />}
      {rewards.isError && <ErrorState {...describeError(rewards.error)} onRetry={() => void rewards.refetch()} />}
      {rewards.isSuccess && rewards.data.length === 0 && (
        <EmptyState icon="gift-off-outline" title="Catálogo vazio" detail="Nenhuma recompensa disponível agora." />
      )}
      {rewards.data?.map((reward, index) => (
        <Animated.View key={reward.id} entering={FadeInDown.delay(index * 70).duration(320)}>
          <RewardTicket
            reward={reward}
            balance={membership.data?.balanceCents ?? null}
            isRedeeming={redemption.isSending && redemption.sendingRewardId === reward.id}
            isBusy={redemption.isSending}
            onRedeem={() => redemption.start(reward.id)}
          />
        </Animated.View>
      ))}
    </Screen>
  );
}

type RewardTicketProps = {
  readonly reward: Reward;
  readonly balance: Cents | null;
  readonly isRedeeming: boolean;
  readonly isBusy: boolean;
  readonly onRedeem: () => void;
};

function RewardTicket({ reward, balance, isRedeeming, isBusy, onRedeem }: RewardTicketProps) {
  const colors = useColors();
  const look = presentReward(reward.id, reward.name);
  const hasDiscount = reward.priceCents < reward.listPriceCents;
  const missing = balance !== null && balance < reward.priceCents ? cents(reward.priceCents - balance) : null;

  return (
    <Ticket
      stub={
        <>
          <MaterialCommunityIcons name={look.icon} size={34} color={colors.primary} />
          {hasDiscount && (
            <View style={[styles.discount, { backgroundColor: colors.accent }]}>
              <Text variant="label" style={{ color: colors.onAccent }}>
                −{reward.discountPercent}%
              </Text>
            </View>
          )}
        </>
      }
    >
      <Text variant="heading">{look.name}</Text>
      {look.blurb.length > 0 && <Text tone="textMuted">{look.blurb}</Text>}
      <View style={styles.prices} accessible accessibilityLabel={priceLabel(reward)}>
        {hasDiscount && (
          <Text tone="textMuted" style={styles.listPrice}>
            {formatMoney(reward.listPriceCents)}
          </Text>
        )}
        <Text variant="title" tone="primary">
          {formatMoney(reward.priceCents)}
        </Text>
      </View>
      {missing && (
        <Text variant="caption" tone="warning">
          Faltam {formatMoney(missing)} no seu saldo
        </Text>
      )}
      <View style={styles.action}>
        <Button
          label={`Resgatar ${look.name}`}
          icon="ticket-confirmation-outline"
          onPress={onRedeem}
          isLoading={isRedeeming}
          isDisabled={isBusy && !isRedeeming}
          compact
        />
      </View>
    </Ticket>
  );
}

function priceLabel(reward: Reward): string {
  const price = `Preço ${formatMoney(reward.priceCents)}`;
  if (reward.priceCents >= reward.listPriceCents) return price;
  return `${price}, de ${formatMoney(reward.listPriceCents)} com ${reward.discountPercent}% de desconto`;
}

function CatalogSkeleton() {
  return (
    <View style={styles.skeleton} accessibilityLabel="Carregando recompensas" accessibilityRole="progressbar">
      {[0, 1, 2].map((placeholder) => (
        <Skeleton key={placeholder} height={150} rounded={radius.lg} />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  discount: { paddingHorizontal: space.sm, paddingVertical: space.xxs, borderRadius: radius.pill },
  prices: { flexDirection: 'row', alignItems: 'baseline', gap: space.sm, flexWrap: 'wrap', marginTop: space.xs },
  listPrice: { textDecorationLine: 'line-through' },
  action: { alignItems: 'flex-start', marginTop: space.sm },
  skeleton: { gap: space.lg },
});
