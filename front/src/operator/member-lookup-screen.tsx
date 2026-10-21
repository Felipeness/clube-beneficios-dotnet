import { useForm, useStore } from '@tanstack/react-form';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { type ReactElement, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { z } from 'zod';
import { reverseTransaction } from '@/api/clube-api';
import { describeError } from '@/api/error-messages';
import { queryKeys } from '@/api/query-client';
import { MembershipId, type StatementLine } from '@/api/schemas';
import { formatPeriod, periodAt } from '@/member/billing-period';
import { useMembershipQuery } from '@/member/member-queries';
import { formatMoney, formatSignedMoney } from '@/money/money';
import type { OperatorSession } from '@/session/session';
import { useOperatorSession } from '@/session/session-provider';
import { describeDescription } from '@/statement/describe-line';
import { StatementList } from '@/statement/statement-list';
import { Button } from '@/ui/button';
import { Card } from '@/ui/card';
import { ConfirmDialog } from '@/ui/confirm-dialog';
import { visibleError } from '@/ui/form-errors';
import { Notice } from '@/ui/notice';
import { Screen, ScreenHeader } from '@/ui/screen';
import { Skeleton } from '@/ui/skeleton';
import { ErrorState } from '@/ui/state-views';
import { Text } from '@/ui/text';
import { TextField } from '@/ui/text-field';
import { radius, space } from '@/ui/tokens';

// The lookup is by exact id and runs on submit, so there is nothing to debounce: no typeahead, no request per keystroke.
const lookupForm = z.object({
  membershipId: z.string().trim().pipe(z.guid('Cole o código inteiro da adesão (8-4-4-4-12 caracteres).')),
});

export function MemberLookupScreen() {
  const session = useOperatorSession();
  const [membershipId, setMembershipId] = useState<MembershipId | null>(null);
  const header = (
    <View style={styles.header}>
      <ScreenHeader
        eyebrow="Balcão"
        title="Atender um membro"
        subtitle="Busque pela adesão, confira saldo e extrato, estorne o que estiver errado."
      />
      <LookupForm current={membershipId} onFound={setMembershipId} />
      {membershipId && <MembershipSummary membershipId={membershipId} session={session} />}
    </View>
  );

  if (!membershipId) return <Screen>{header}</Screen>;
  return <ReversibleStatement key={membershipId} membershipId={membershipId} session={session} header={header} />;
}

type LookupFormProps = { readonly current: MembershipId | null; readonly onFound: (id: MembershipId) => void };

// The header moves from a plain screen into the statement list after the first search, which remounts it; starting
// from the current id keeps the field filled across that move.
function LookupForm({ current, onFound }: LookupFormProps) {
  const form = useForm({
    defaultValues: { membershipId: current ?? '' },
    validators: { onChange: lookupForm },
    onSubmit: ({ value }) => onFound(MembershipId.parse(value.membershipId.trim())),
  });
  const submissionAttempts = useStore(form.store, (state) => state.submissionAttempts);
  return (
    <Card>
      <form.Field name="membershipId">
        {(field) => (
          <TextField
            label="Código da adesão"
            placeholder="0199f0a4-7c1e-7000-8000-…"
            value={field.state.value}
            onChangeText={field.handleChange}
            onBlur={field.handleBlur}
            onSubmitEditing={() => void form.handleSubmit()}
            error={visibleError(field.state.meta, submissionAttempts)}
            autoCapitalize="none"
            isMonospace
          />
        )}
      </form.Field>
      <Button label="Buscar" icon="magnify" onPress={() => void form.handleSubmit()} />
    </Card>
  );
}

type MembershipProps = { readonly membershipId: MembershipId; readonly session: OperatorSession };

function MembershipSummary({ membershipId, session }: MembershipProps) {
  const membership = useMembershipQuery(membershipId, session.token);
  if (membership.isPending) return <Skeleton height={96} rounded={radius.lg} />;
  if (membership.isError) {
    return <ErrorState {...describeError(membership.error)} onRetry={() => void membership.refetch()} />;
  }
  return (
    <Card>
      <View style={styles.summary}>
        <View style={styles.summaryText}>
          <Text variant="label" tone="textMuted">
            TITULAR
          </Text>
          <Text variant="mono">{membership.data.holder}</Text>
          <Text variant="caption" tone="textMuted">
            Membro desde {formatPeriod(periodAt(membership.data.subscribedAt))}
          </Text>
        </View>
        <View style={styles.balance}>
          <Text variant="label" tone="textMuted">
            SALDO
          </Text>
          <Text variant="title" tone="primary">
            {formatMoney(membership.data.balanceCents)}
          </Text>
        </View>
      </View>
    </Card>
  );
}

type ReversibleStatementProps = MembershipProps & { readonly header: ReactElement };

function ReversibleStatement({ membershipId, session, header }: ReversibleStatementProps) {
  const queryClient = useQueryClient();
  const [pending, setPending] = useState<StatementLine | null>(null);
  const reverse = useMutation({
    mutationFn: (line: StatementLine) => reverseTransaction(membershipId, line.transactionId, { token: session.token }),
    onSuccess: () => setPending(null),
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.membership(membershipId) }),
  });

  const headerWithOutcome = (
    <View style={styles.header}>
      {header}
      {reverse.isSuccess && (
        <Notice
          tone="success"
          title="Estorno lançado"
          detail={`Saldo agora: ${formatMoney(reverse.data.balanceCents)}. O estorno aparece no topo do extrato.`}
        />
      )}
      {reverse.isError && <Notice tone="error" {...describeError(reverse.error)} />}
      <Text variant="heading">Extrato</Text>
    </View>
  );

  return (
    <>
      <StatementList
        membershipId={membershipId}
        token={session.token}
        perspective="operator"
        header={headerWithOutcome}
        onReverse={(line) => {
          reverse.reset();
          setPending(line);
        }}
      />
      <ConfirmDialog
        isOpen={pending !== null}
        title="Estornar este lançamento?"
        message={
          pending
            ? `${describeDescription(pending.description)} (${formatSignedMoney(pending.amountCents)}). O estorno é um lançamento novo, em sentido contrário, registrado em seu nome. O original continua no extrato.`
            : ''
        }
        confirmLabel="Estornar"
        isConfirming={reverse.isPending}
        onConfirm={() => pending && reverse.mutate(pending)}
        onCancel={() => setPending(null)}
      />
    </>
  );
}

const styles = StyleSheet.create({
  header: { gap: space.lg, paddingBottom: space.md },
  summary: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-end',
    gap: space.md,
    flexWrap: 'wrap',
  },
  summaryText: { gap: space.xxs },
  balance: { alignItems: 'flex-end', gap: space.xxs },
});
