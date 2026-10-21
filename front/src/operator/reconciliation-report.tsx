import { StyleSheet, View } from 'react-native';
import type { DivergenceKind, Reconciliation } from '@/api/schemas';
import { type Cents, formatMoney } from '@/money/money';
import { Notice } from '@/ui/notice';
import { Text } from '@/ui/text';
import { useColors } from '@/ui/theme';
import { type ColorTokens, radius, space } from '@/ui/tokens';

type KindLook = { readonly title: string; readonly meaning: string; readonly color: (colors: ColorTokens) => string };

// Ordered by how bad each one is for the member: paid and not credited comes first.
const kinds: Record<DivergenceKind, KindLook> = {
  MissingInLedger: {
    title: 'Pago e não creditado',
    meaning: 'O PSP liquidou, o livro não creditou: o membro pagou e não recebeu.',
    color: (colors) => colors.danger,
  },
  MissingAtPsp: {
    title: 'Creditado sem dinheiro',
    meaning: 'O livro creditou um Pix que o PSP não liquidou nesse dia.',
    color: (colors) => colors.warning,
  },
  AmountMismatch: {
    title: 'Valores diferentes',
    meaning: 'O mesmo Pix tem um valor no PSP e outro no livro.',
    color: (colors) => colors.warning,
  },
};

const amountOrDash = (amount: Cents | null): string => (amount === null ? '—' : formatMoney(amount));

export function ReconciliationReport({ report }: { readonly report: Reconciliation }) {
  const colors = useColors();
  return (
    <View style={styles.report}>
      <View style={styles.counts}>
        <Count label="Liquidados no PSP" value={report.settledAtPsp} />
        <Count label="Creditados no livro" value={report.creditedInLedger} />
      </View>
      {report.divergences.length === 0 ? (
        <Notice tone="success" title="Tudo bate" detail={`Nenhuma divergência em ${report.day}.`} />
      ) : (
        report.divergences.map((divergence) => {
          const look = kinds[divergence.kind];
          return (
            <View
              key={`${divergence.kind}-${divergence.endToEndId}`}
              style={[
                styles.divergence,
                { borderLeftColor: look.color(colors), backgroundColor: colors.surfaceSunken },
              ]}
            >
              <Text variant="bodyStrong" style={{ color: look.color(colors) }}>
                {look.title}
              </Text>
              <Text variant="caption" tone="textMuted">
                {look.meaning}
              </Text>
              <Text variant="mono" selectable>
                {divergence.endToEndId}
              </Text>
              <Text variant="caption">
                PSP {amountOrDash(divergence.pspCents)} · Livro {amountOrDash(divergence.ledgerCents)}
              </Text>
            </View>
          );
        })
      )}
    </View>
  );
}

function Count({ label, value }: { readonly label: string; readonly value: number }) {
  const colors = useColors();
  return (
    <View
      style={[styles.count, { backgroundColor: colors.surfaceSunken }]}
      accessible
      accessibilityLabel={`${label}: ${value}`}
    >
      <Text variant="title">{value}</Text>
      <Text variant="caption" tone="textMuted">
        {label}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  report: { gap: space.md },
  counts: { flexDirection: 'row', gap: space.md },
  count: { flex: 1, padding: space.md, borderRadius: radius.md, gap: space.xxs },
  divergence: { borderLeftWidth: 4, borderRadius: radius.sm, padding: space.md, gap: space.xxs },
});
