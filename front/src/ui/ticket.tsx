import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { useColors } from './theme';
import { radius, space } from './tokens';

type TicketTone = 'paper' | 'green';

type TicketProps = {
  readonly children: ReactNode;
  readonly stub: ReactNode;
  readonly tone?: TicketTone;
  readonly isHighlighted?: boolean;
};

const NOTCH = 22;
const STUB_WIDTH = 104;

// The club's signature shape: a coupon with a perforated stub. The notches are circles painted in the page color, so
// the ticket looks punched out of the background on any theme.
export function Ticket({ children, stub, tone = 'paper', isHighlighted = false }: TicketProps) {
  const colors = useColors();
  const body = tone === 'green' ? colors.card : colors.surface;
  const edge = isHighlighted ? colors.accent : tone === 'green' ? colors.card : colors.border;
  const perforation = tone === 'green' ? colors.onCardMuted : colors.border;

  return (
    <View style={[styles.ticket, { backgroundColor: body, borderColor: edge }]}>
      <View style={styles.main}>{children}</View>
      <View style={[styles.perforation, { borderColor: perforation }]} />
      <View style={styles.stub}>{stub}</View>
      <View style={[styles.notch, styles.notchTop, { backgroundColor: colors.background, borderColor: edge }]} />
      <View style={[styles.notch, styles.notchBottom, { backgroundColor: colors.background, borderColor: edge }]} />
    </View>
  );
}

const styles = StyleSheet.create({
  ticket: { flexDirection: 'row', borderRadius: radius.lg, borderWidth: 2, overflow: 'hidden', minHeight: 120 },
  main: { flex: 1, padding: space.lg, gap: space.xs, justifyContent: 'center' },
  perforation: { width: 0, borderLeftWidth: 2, borderStyle: 'dashed', marginVertical: NOTCH / 2 + space.xs },
  stub: { width: STUB_WIDTH, padding: space.md, alignItems: 'center', justifyContent: 'center', gap: space.xs },
  notch: {
    position: 'absolute',
    width: NOTCH,
    height: NOTCH,
    borderRadius: NOTCH / 2,
    borderWidth: 2,
    right: STUB_WIDTH - NOTCH / 2 + 1,
  },
  notchTop: { top: -NOTCH / 2 - 2 },
  notchBottom: { bottom: -NOTCH / 2 - 2 },
});
