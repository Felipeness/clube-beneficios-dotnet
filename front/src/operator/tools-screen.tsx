import { useMutation, useQueryClient } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import {
  type ClockChange,
  changeDevClock,
  runCreditExpiryJob,
  runOutboxJob,
  runReconciliationJob,
} from '@/api/clube-api';
import { describeError } from '@/api/error-messages';
import { queryKeys } from '@/api/query-client';
import type { CreditExpiryRun, OutboxRun } from '@/api/schemas';
import { dayAt } from '@/member/billing-period';
import { useDevClockQuery } from '@/member/member-queries';
import { formatMoney } from '@/money/money';
import { Button, type IconName } from '@/ui/button';
import { Card } from '@/ui/card';
import { Notice } from '@/ui/notice';
import { Screen, ScreenHeader } from '@/ui/screen';
import { Skeleton } from '@/ui/skeleton';
import { ErrorState } from '@/ui/state-views';
import { Text } from '@/ui/text';
import { useColors } from '@/ui/theme';
import { radius, space } from '@/ui/tokens';
import { ReconciliationReport } from './reconciliation-report';

export function ToolsScreen() {
  const clock = useDevClockQuery();
  const today = dayAt(clock.data?.now ?? new Date());
  return (
    <Screen>
      <ScreenHeader
        eyebrow="Ferramentas"
        title="Bastidores do Clube"
        subtitle="Só existem em desenvolvimento: rodam os jobs agora e movem o relógio do servidor."
      />
      <ClockCard />
      <JobCard
        title="Entregar vouchers (outbox)"
        detail="Envia aos parceiros os vouchers dos resgates pendentes; falhas são reagendadas com recuo."
        icon="send-outline"
        run={runOutboxJob}
        report={(run) => <OutboxReport run={run} />}
      />
      <JobCard
        title="Expirar créditos"
        detail="Créditos com mais de 12 meses saem do saldo, os mais antigos primeiro."
        icon="timer-sand"
        run={runCreditExpiryJob}
        report={(run) => <ExpiryReport run={run} />}
      />
      <JobCard
        title={`Reconciliar ${today}`}
        detail="Roda a reconciliação do dia de hoje no relógio do servidor, como o worker diário faria."
        icon="scale-balance"
        run={() => runReconciliationJob(today)}
        report={(report) => <ReconciliationReport report={report} />}
      />
    </Screen>
  );
}

const clockMoves: ReadonlyArray<{ readonly label: string; readonly icon: IconName; readonly change: ClockChange }> = [
  { label: '+1 mês', icon: 'calendar-arrow-right', change: { advanceDays: 30 } },
  { label: '+12 meses', icon: 'calendar-end', change: { advanceDays: 365 } },
  { label: 'Voltar a hoje', icon: 'calendar-refresh-outline', change: { reset: true } },
];

const clockFormat = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'long', timeStyle: 'short' });

function ClockCard() {
  const queryClient = useQueryClient();
  const clock = useDevClockQuery();
  // Moving time changes which periods are payable and what can expire: every cached answer is now suspect.
  const move = useMutation({
    mutationFn: (change: ClockChange) => changeDevClock(change),
    onSuccess: () => queryClient.invalidateQueries(),
  });
  return (
    <Card>
      <Text variant="heading">Relógio do servidor</Text>
      <Text tone="textMuted">
        Avance o tempo para mostrar a janela de competência (não dá para pagar um mês futuro) e a expiração de 12 meses.
      </Text>
      {clock.isPending && <Skeleton height={56} />}
      {clock.isError && <ErrorState {...describeError(clock.error)} onRetry={() => void clock.refetch()} />}
      {clock.isSuccess && (
        <View accessibilityLiveRegion="polite">
          <Text variant="title">{clockFormat.format(clock.data.now)}</Text>
          <Text variant="caption" tone={clock.data.offsetDays > 0 ? 'accent' : 'textMuted'}>
            {clock.data.offsetDays > 0 ? `${clock.data.offsetDays} dias à frente do relógio real` : 'No horário real'}
          </Text>
        </View>
      )}
      <View style={styles.moves}>
        {clockMoves.map((option) => (
          <Button
            key={option.label}
            label={option.label}
            icon={option.icon}
            variant="secondary"
            onPress={() => move.mutate(option.change)}
            isDisabled={move.isPending}
            compact
          />
        ))}
      </View>
      {move.isError && <Notice tone="error" {...describeError(move.error)} />}
    </Card>
  );
}

type JobCardProps<Report> = {
  readonly title: string;
  readonly detail: string;
  readonly icon: IconName;
  readonly run: () => Promise<Report>;
  readonly report: (report: Report) => ReactNode;
};

function JobCard<Report>({ title, detail, icon, run, report }: JobCardProps<Report>) {
  const queryClient = useQueryClient();
  // Jobs move money (expiry) and statuses (outbox): refresh whatever is on screen afterwards.
  const job = useMutation({
    mutationFn: run,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.allMemberships }),
  });
  return (
    <Card>
      <Text variant="heading">{title}</Text>
      <Text tone="textMuted">{detail}</Text>
      <View style={styles.run}>
        <Button label="Rodar agora" icon={icon} onPress={() => job.mutate()} isLoading={job.isPending} compact />
      </View>
      {job.isSuccess && report(job.data)}
      {job.isError && <Notice tone="error" {...describeError(job.error)} />}
    </Card>
  );
}

function OutboxReport({ run }: { readonly run: OutboxRun }) {
  if (run.attempted === 0)
    return <Notice tone="info" title="Nada na fila" detail="Nenhum voucher esperando entrega." />;
  return (
    <Figures
      figures={[
        ['Tentados', String(run.attempted)],
        ['Entregues', String(run.delivered)],
        ['Reagendados', String(run.rescheduled)],
        ['Dead letter', String(run.deadLettered)],
      ]}
    />
  );
}

function ExpiryReport({ run }: { readonly run: CreditExpiryRun }) {
  return (
    <View style={styles.report}>
      <Figures
        figures={[
          ['Corte', run.cutoff],
          ['Membros', String(run.expired)],
          ['Expirado', formatMoney(run.expiredCents)],
        ]}
      />
      {run.failures.map((failure) => (
        <Notice
          key={failure.membershipId}
          tone="error"
          title={`Falhou: ${failure.membershipId}`}
          detail={failure.error}
        />
      ))}
    </View>
  );
}

function Figures({ figures }: { readonly figures: ReadonlyArray<readonly [string, string]> }) {
  const colors = useColors();
  return (
    <View style={styles.figures}>
      {figures.map(([label, value]) => (
        <View
          key={label}
          style={[styles.figure, { backgroundColor: colors.surfaceSunken }]}
          accessible
          accessibilityLabel={`${label}: ${value}`}
        >
          <Text variant="heading">{value}</Text>
          <Text variant="caption" tone="textMuted">
            {label}
          </Text>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  moves: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
  run: { alignItems: 'flex-start' },
  report: { gap: space.md },
  figures: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
  figure: { flexGrow: 1, minWidth: 120, padding: space.md, borderRadius: radius.md, gap: space.xxs },
});
