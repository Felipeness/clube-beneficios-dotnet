import { Pressable, StyleSheet } from 'react-native';
import { Text } from './text';
import { useColors } from './theme';
import { minTouchTarget, radius, space } from './tokens';

type ChipProps = {
  readonly label: string;
  readonly isSelected: boolean;
  readonly onPress: () => void;
  readonly accessibilityLabel?: string;
};

export function Chip({ label, isSelected, onPress, accessibilityLabel }: ChipProps) {
  const colors = useColors();
  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityState={{ checked: isSelected }}
      onPress={onPress}
      style={({ pressed }) => [
        styles.chip,
        {
          backgroundColor: isSelected ? colors.primary : colors.surface,
          borderColor: isSelected ? colors.primary : colors.border,
          opacity: pressed ? 0.8 : 1,
        },
      ]}
    >
      <Text variant="bodyStrong" style={{ color: isSelected ? colors.onPrimary : colors.text }}>
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  chip: {
    minHeight: minTouchTarget,
    paddingHorizontal: space.lg,
    borderRadius: radius.pill,
    borderWidth: 1.5,
    justifyContent: 'center',
  },
});
