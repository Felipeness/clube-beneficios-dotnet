import MaterialCommunityIcons from '@expo/vector-icons/MaterialCommunityIcons';
import type { ReactElement } from 'react';
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, View } from 'react-native';
import { describeError } from '@/api/error-messages';
import type { AccessToken, MembershipId, StatementLine } from '@/api/schemas';
import { useStatementQuery } from '@/member/member-queries';
import { formatSignedMoney } from '@/money/money';
import { Button } from '@/ui/button';
import { columnStyle } from '@/ui/screen';
import { Skeleton } from '@/ui/skeleton';
import { EmptyState, ErrorState } from '@/ui/state-views';
import { Text } from '@/ui/text';
import { useColors } from '@/ui/theme';
import { radius, space } from '@/ui/tokens';
import { describeActor, describeDescription, formatPostedAt, isReversal, type Perspective } from './describe-line';

type StatementListProps = {
  readonly membershipId: MembershipId;
  readonly token: AccessToken;
  readonly perspective: Perspective;
  readonly header: ReactElement;
  readonly onReverse?: (line: StatementLine) => void;
};

// FlatList, not FlashList: a demo statement is tens of lines, FlatList already virtualizes, and it is one fewer native
// dependency to keep in step with the SDK. The next page loads when the end is near, through the cursor the API hands out.
export function StatementList({ membershipId, token, perspective, header, onReverse }: StatementListProps) {
  const colors = useColors();
  const statement = useStatementQuery(membershipId, token);
  const lines = statement.data?.pages.flatMap((page) => page.lines) ?? [];

  const loadMore = () => {
    if (statement.hasNextPage && !statement.isFetchingNextPage) void statement.fetchNextPage();
  };

  return (
    <FlatList
      data={lines}
      keyExtractor={(line) => line.transactionId}
      renderItem={({ item }) => <StatementRow line={item} perspective={perspective} onReverse={onReverse} />}
      ItemSeparatorComponent={() => <View style={[styles.separator, { backgroundColor: colors.border }]} />}
      ListHeaderComponent={header}
      ListEmptyComponent={<StatementPlaceholder status={statement} />}
      ListFooterComponent={
        statement.isFetchingNextPage ? <ActivityIndicator color={colors.primary} style={styles.footer} /> : null
      }
      onEndReached={loadMore}
      onEndReachedThreshold={0.4}
      refreshControl={
        <RefreshControl
          refreshing={statement.isRefetching && !statement.isFetchingNextPage}
          onRefresh={() => void statement.refetch()}
          tintColor={colors.primary}
        />
      }
      style={{ backgroundColor: colors.background }}
      contentContainerStyle={[columnStyle, styles.content]}
    />
  );
}

type PlaceholderProps = { readonly status: ReturnType<typeof useStatementQuery> };

function StatementPlaceholder({ status }: PlaceholderProps) {
  if (status.isPending) {
    return (
      <View style={styles.skeleton} accessibilityLabel="Carregando extrato" accessibilityRole="progressbar">
        {[0, 1, 2, 3].map((placeholder) => (
          <Skeleton key={placeholder} height={56} rounded={radius.md} />
        ))}
      </View>
    );
  }
  if (status.isError) return <ErrorState {...describeError(status.error)} onRetry={() => void status.refetch()} />;
  return (
    <EmptyState
      icon="receipt-text-outline"
      title="Nada por aqui ainda"
      detail="O primeiro lançamento aparece quando o Pix da mensalidade for confirmado."
    />
  );
}

type StatementRowProps = {
  readonly line: StatementLine;
  readonly perspective: Perspective;
  readonly onReverse: ((line: StatementLine) => void) | undefined;
};

function StatementRow({ line, perspective, onReverse }: StatementRowProps) {
  const colors = useColors();
  const isCredit = line.amountCents > 0;
  const description = describeDescription(line.description);
  const actor = describeActor(line, perspective);
  const when = formatPostedAt(line.postedAt);
  const amount = formatSignedMoney(line.amountCents);

  return (
    <View style={styles.row}>
      <View
        style={styles.rowMain}
        accessible
        accessibilityLabel={`${description}, ${isCredit ? 'entrada' : 'saída'} de ${amount}, ${when}, por ${actor}`}
      >
        <View style={[styles.icon, { backgroundColor: isCredit ? colors.primarySoft : colors.dangerSoft }]}>
          <MaterialCommunityIcons
            name={isCredit ? 'arrow-bottom-left' : 'arrow-top-right'}
            size={18}
            color={isCredit ? colors.credit : colors.debit}
          />
        </View>
        <View style={styles.texts}>
          <Text variant="bodyStrong" numberOfLines={2}>
            {description}
          </Text>
          <Text variant="caption" tone="textMuted">
            {when} · {actor}
          </Text>
        </View>
        <Text variant="bodyStrong" tone={isCredit ? 'credit' : 'debit'}>
          {amount}
        </Text>
      </View>
      {onReverse && !isReversal(line) && (
        <View style={styles.reverse}>
          <Button
            label="Estornar"
            icon="undo-variant"
            variant="danger"
            onPress={() => onReverse(line)}
            accessibilityHint={`Estorna ${description}`}
            compact
          />
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  content: { paddingBottom: space.xxxl * 2 },
  separator: { height: StyleSheet.hairlineWidth },
  footer: { paddingVertical: space.lg },
  skeleton: { gap: space.sm },
  row: { paddingVertical: space.md, gap: space.sm },
  rowMain: { flexDirection: 'row', alignItems: 'center', gap: space.md },
  icon: { width: 36, height: 36, borderRadius: radius.pill, alignItems: 'center', justifyContent: 'center' },
  texts: { flex: 1, gap: space.xxs },
  reverse: { alignItems: 'flex-end' },
});
