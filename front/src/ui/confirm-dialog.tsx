import { Modal, Pressable, StyleSheet, View } from 'react-native';
import Animated, { ZoomIn } from 'react-native-reanimated';
import { Button } from './button';
import { Text } from './text';
import { useColors } from './theme';
import { radius, space } from './tokens';

type ConfirmDialogProps = {
  readonly isOpen: boolean;
  readonly title: string;
  readonly message: string;
  readonly confirmLabel: string;
  readonly isConfirming: boolean;
  readonly onConfirm: () => void;
  readonly onCancel: () => void;
};

// Alert.alert does nothing on react-native-web, so destructive confirmations get one dialog that works everywhere.
export function ConfirmDialog(props: ConfirmDialogProps) {
  const colors = useColors();
  return (
    <Modal transparent visible={props.isOpen} animationType="fade" onRequestClose={props.onCancel}>
      <View style={styles.center}>
        <Pressable
          accessibilityLabel="Fechar"
          onPress={props.onCancel}
          style={[StyleSheet.absoluteFill, { backgroundColor: colors.overlay }]}
        />
        <Animated.View
          entering={ZoomIn.duration(180)}
          accessibilityViewIsModal
          accessibilityRole="alert"
          style={[styles.dialog, { backgroundColor: colors.surface }]}
        >
          <Text variant="heading">{props.title}</Text>
          <Text tone="textMuted">{props.message}</Text>
          <View style={styles.actions}>
            <Button label="Cancelar" variant="secondary" onPress={props.onCancel} compact />
            <Button
              label={props.confirmLabel}
              variant="danger"
              onPress={props.onConfirm}
              isLoading={props.isConfirming}
              compact
            />
          </View>
        </Animated.View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: space.lg },
  dialog: { width: '100%', maxWidth: 420, borderRadius: radius.lg, padding: space.xl, gap: space.md },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: space.sm, flexWrap: 'wrap', marginTop: space.sm },
});
