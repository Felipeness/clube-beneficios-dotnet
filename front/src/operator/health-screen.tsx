import MaterialCommunityIcons from '@expo/vector-icons/MaterialCommunityIcons';
import { useForm, useStore } from '@tanstack/react-form';
import { skipToken, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';
import { z } from 'zod';
import { getLedgerHealth, getReconciliation } from '@/api/clube-api';
import { describeError } from '@/api/error-messages';
import { queryKeys } from '@/api/query-client';
import type { LedgerHealth } from '@/api/schemas';
import { dayAt } from '@/member/billing-period';
import { useDevClockQuery } from '@/member/member-queries';
import { cents, formatMoney } from '@/money/money';
import { useOperatorSession } from '@/session/session-provider';
import { Button } from '@/ui/button';
import { Card } from '@/ui/card';
import { Chip } from '@/ui/chip';
import { visibleError } from '@/ui/form-errors';
import { Screen, ScreenHeader } from '@/ui/screen';
import { Skeleton } from '@/ui/skeleton';
import { ErrorState } from '@/ui/state-views';
import { Text } from '@/ui/text';
import { TextField } from '@/ui/text-field';
import { useColors } from '@/ui/theme';
import { type ColorTokens, radius, space } from '@/ui/tokens';
import { ReconciliationReport } from './reconciliation-report';

export function HealthScreen() {
  const health = useQuery({
    queryKey: queryKeys.ledgerHealth,
    queryFn: ({ signal }) => getLedgerHealth({ signal }),
    refetchInterval: 30_000,
  });
  const colors = useColors();
  return (
    <Screen
      refreshControl={
        <RefreshControl
          refreshing={health.isRefetching}
          onRefresh={() => void health.refetch()}
          tintColor={colors.primary}
        />
      }
    >
      <ScreenHeader
        eyebrow="Saúde"
        title="O livro-caixa fecha?"
        subtitle="Partidas dobradas: a soma de todos os lançamentos tem que dar zero."
      />
      {health.isPending && <Skeleton height={120} rounded={radius.lg} />}
      {health.isError && <ErrorState {...describeError(health.error)} onRetry={() => void health.refetch()} />}
      {health.isSuccess && <HealthCard health={health.data} />}
      <ReconciliationCard />
    </Screen>
  );
}

type StatusLook = {
  readonly title: string;
  readonly icon: 'check-decagram' | 'alert' | 'alert-octagon';
  readonly tint: (colors: ColorTokens) => string;
};

const statusLooks: Record<LedgerHealth['status'], StatusLook> = {
  Healthy: { title: 'Equilibrado', icon: 'check-decagram', tint: (colors) => colors.credit },
  Degraded: { title: 'Degradado', icon: 'alert', tint: (colors) => colors.warning },
  Unhealthy: { title: 'Fora de equilíbrio', icon: 'alert-octagon', tint: (colors) => colors.danger },
};

function HealthCard({ health }: { readonly health: LedgerHealth }) {
  const colors = useColors();
  const look = statusLooks[health.status];
  const difference = health.differenceCents ?? cents(0);
  return (
    <Card accessibilityLabel={`Livro-caixa ${look.title}`}>
      <View style={styles.status}>
        <MaterialCommunityIcons name={look.icon} size={40} color={look.tint(colors)} />
        <View style={styles.statusText}>
          <Text variant="title" style={{ color: look.tint(colors) }}>
            {look.title}
          </Text>
          <Text tone="textMuted">
            {health.status === 'Healthy'
              ? 'Todos os lançamentos somam zero.'
              : `Os lançamentos somam ${formatMoney(difference)} (${difference} centavos) em vez de zero.`}
          </Text>
        </View>
      </View>
    </Card>
  );
}

const ONE_DAY_MS = 24 * 60 * 60 * 1000;

const reconciliationForm = z.object({ date: z.iso.date('Use o formato AAAA-MM-DD.') });

function ReconciliationCard() {
  const session = useOperatorSession();
  const clock = useDevClockQuery();
  const now = clock.data?.now ?? new Date();
  const today = dayAt(now);
  const yesterday = dayAt(new Date(now.getTime() - ONE_DAY_MS));
  const [date, setDate] = useState<string | null>(null);
  const form = useForm({
    defaultValues: { date: '' },
    validators: { onChange: reconciliationForm },
    onSubmit: ({ value }) => setDate(value.date),
  });
  const submissionAttempts = useStore(form.store, (state) => state.submissionAttempts);
  const report = useQuery({
    queryKey: queryKeys.reconciliation(date),
    queryFn: date === null ? skipToken : ({ signal }) => getReconciliation(date, { token: session.token, signal }),
  });

  const pick = (day: string) => {
    form.setFieldValue('date', day);
    void form.handleSubmit();
  };

  return (
    <Card>
      <Text variant="heading">Reconciliação com o PSP</Text>
      <Text tone="textMuted">Compara o que o PSP liquidou num dia com o que o livro creditou.</Text>
      <View style={styles.quickDays}>
        <Chip label="Hoje" isSelected={date === today} onPress={() => pick(today)} />
        <Chip label="Ontem" isSelected={date === yesterday} onPress={() => pick(yesterday)} />
      </View>
      <form.Field name="date">
        {(field) => (
          <TextField
            label="Dia (São Paulo)"
            placeholder={today}
            keyboardType="numbers-and-punctuation"
            maxLength={10}
            value={field.state.value}
            onChangeText={field.handleChange}
            onBlur={field.handleBlur}
            onSubmitEditing={() => void form.handleSubmit()}
            error={visibleError(field.state.meta, submissionAttempts)}
            isMonospace
          />
        )}
      </form.Field>
      <Button
        label="Reconciliar"
        icon="scale-balance"
        onPress={() => void form.handleSubmit()}
        isLoading={report.isFetching}
      />
      {report.isError && <ErrorState {...describeError(report.error)} onRetry={() => void report.refetch()} />}
      {report.isSuccess && <ReconciliationReport report={report.data} />}
    </Card>
  );
}

const styles = StyleSheet.create({
  status: { flexDirection: 'row', alignItems: 'center', gap: space.lg },
  statusText: { flex: 1, gap: space.xxs },
  quickDays: { flexDirection: 'row', gap: space.sm },
});
