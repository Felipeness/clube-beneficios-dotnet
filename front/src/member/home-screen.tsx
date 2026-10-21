import { RefreshControl, StyleSheet, View } from 'react-native';
import { describeError } from '@/api/error-messages';
import { useMemberSession } from '@/session/session-provider';
import { Screen, ScreenHeader } from '@/ui/screen';
import { Skeleton } from '@/ui/skeleton';
import { ErrorState } from '@/ui/state-views';
import { useColors } from '@/ui/theme';
import { radius, space } from '@/ui/tokens';
import { useDevClockQuery, useMembershipQuery } from './member-queries';
import { MembershipCard } from './membership-card';
import { PixCard } from './pix-card';

export function HomeScreen() {
  const session = useMemberSession();
  const membership = useMembershipQuery(session.membershipId, session.token);
  const clock = useDevClockQuery();
  const colors = useColors();

  const refresh = () => Promise.all([membership.refetch(), clock.refetch()]);

  return (
    <Screen
      refreshControl={
        <RefreshControl refreshing={membership.isRefetching} onRefresh={refresh} tintColor={colors.primary} />
      }
    >
      <ScreenHeader eyebrow="Início" title="Olá, membro" subtitle="Seu saldo, sua carteirinha e o Pix do mês." />
      {membership.isPending && <HomeSkeleton />}
      {membership.isError && (
        <ErrorState {...describeError(membership.error)} onRetry={() => void membership.refetch()} />
      )}
      {membership.isSuccess && (
        <>
          <MembershipCard membership={membership.data} />
          {/* Until the dev clock answers, the device clock decides which month is "now". */}
          <PixCard membership={membership.data} now={clock.data?.now ?? new Date()} />
        </>
      )}
    </Screen>
  );
}

function HomeSkeleton() {
  return (
    <View style={styles.skeleton} accessibilityLabel="Carregando saldo" accessibilityRole="progressbar">
      <Skeleton height={220} rounded={radius.xl} />
      <Skeleton height={180} rounded={radius.lg} />
    </View>
  );
}

const styles = StyleSheet.create({ skeleton: { gap: space.lg, marginTop: space.lg } });
